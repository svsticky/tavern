using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Backend.Controllers.DTOs;
using Backend.Database;
using Backend.Interfaces;
using Backend.Models;
using Backend.Models.Domain;
using Backend.Services.Domain;
using Backend.Utils.DateTime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Backend.Tests.Services.Domain;

public class StatisticsServiceTests : IDisposable
{
    private readonly PostgresDbContext _db;
    private readonly IPermissionService _permissionService;
    private readonly StatisticsService _service;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _originalCommitteeCreationDate;

    private readonly Study _cs = new() { Id = 1, Title = "Computer Science", NominalDurationYears = 3, Type = StudyType.Bachelor };
    private readonly Study _math = new() { Id = 2, Title = "Mathematics", NominalDurationYears = 3, Type = StudyType.Bachelor };
    private readonly Study _csMaster = new() { Id = 3, Title = "CS Master", NominalDurationYears = 2, Type = StudyType.Master };

    public StatisticsServiceTests()
    {
        _originalCommitteeCreationDate = YearUtils.CommitteeCreationDate;
        YearUtils.CommitteeCreationDate = "08-01";

        var options = new DbContextOptionsBuilder<PostgresDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new PostgresDbContext(options);
        _db.Database.EnsureCreated();
        _db.Studies.AddRange(_cs, _math, _csMaster);
        _db.SaveChanges();

        _permissionService = Substitute.For<IPermissionService>();
        _service = new StatisticsService(_db, _permissionService, NullLogger<StatisticsService>.Instance);
    }

    public void Dispose()
    {
        YearUtils.CommitteeCreationDate = _originalCommitteeCreationDate;
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    private static DateTimeOffset D(int year, int month, int day) => new(year, month, day, 12, 0, 0, TimeSpan.Zero);

    private Member AddMember(DateTimeOffset registeredOn, bool isDeleted = false, bool ereLid = false, bool lidVanVerdienste = false, bool gratie = false, bool begunstiger = false)
    {
        var id = Guid.NewGuid();
        var member = new Member
        {
            Id = id,
            StudentNumber = id.ToString(),
            FirstName = "Test",
            LastName = "User",
            Email = $"{id}@example.com",
            PhoneNumber = "0612345678",
            Street = "Street",
            HouseNumber = "1",
            PostalCode = "1234AB",
            City = "City",
            DateOfBirth = D(2000, 1, 1),
            RegisteredOn = registeredOn,
            IsDeleted = isDeleted,
            EreLid = ereLid,
            LidVanVerdienste = lidVanVerdienste,
            Gratie = gratie,
            Begunstiger = begunstiger,
        };
        _db.Members.Add(member);
        return member;
    }

    private void AddStudyEnrollment(Member member, Study study, DateTimeOffset start, StudyStatus status = StudyStatus.Enrolled, DateTimeOffset? completion = null)
    {
        _db.StudyEnrollments.Add(new StudyEnrollment
        {
            MemberId = member.Id,
            StudyId = study.Id,
            EnrollmentDate = start,
            CompletionDate = completion,
            Status = status,
        });
    }

    private Group AddGroup(string name, GroupType type = GroupType.Committee)
    {
        var group = new Group { Name = name, Type = type };
        _db.Groups.Add(group);
        return group;
    }

    private void AddGroupMembership(Member member, Group group, uint year)
    {
        _db.GroupMemberships.Add(new GroupMembership { MemberId = member.Id, Group = group, MembershipYear = year });
    }

    private Activity AddActivity(DateTimeOffset start, string name = "Activity")
    {
        var activity = new Activity
        {
            Name = name,
            DutchDescription = "Beschrijving",
            EnglishDescription = "Description",
            DateTimeStart = start,
            DateTimeEnd = start.AddHours(2),
            IsEnrollable = true,
            ShowInKoala = true,
            AllowedAudience = TargetAudience.All,
            PaymentDeadline = start.AddDays(14),
            Location = "Enschede",
        };
        _db.Activities.Add(activity);
        return activity;
    }

    private void AddEnrollment(Member member, Activity activity, bool waitingList = false)
    {
        _db.Enrollments.Add(new Enrollment
        {
            ActivityId = activity.Id,
            Activity = activity,
            MemberId = member.Id,
            RegisteredOn = DateTime.UtcNow,
            IsOnWaitingList = waitingList,
        });
    }

    private Task<StatisticsResponseDTO> Get(params StatisticType[] types)
        => _service.GetStatistics(types.ToHashSet(), null, _userId, CancellationToken.None);

    private static int CurrentTotal(StatisticsResponseDTO result) => result.TotalMembersPerYear!.Last().Count;

    private Task<StatisticsResponseDTO> GetForYear(uint year, params StatisticType[] types)
        => _service.GetStatistics(types.ToHashSet(), year, _userId, CancellationToken.None);

    [Fact]
    public async Task GetStatistics_NotBoard_Throws()
    {
        _permissionService.When(p => p.EnsureBoardOrCandidateBoardMember(_userId)).Do(_ => throw new UnauthorizedAccessException());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Get(StatisticType.TotalMembersPerYear));
    }

    [Fact]
    public async Task GetStatistics_SingleStatistic_OnlyPopulatesRequested()
    {
        var result = await Get(StatisticType.TotalMembersPerYear);

        Assert.Equal(0, Assert.Single(result.TotalMembersPerYear!).Count);
        Assert.Null(result.ActiveEnrollmentsPerStudy);
        Assert.Null(result.NewMembersPerYear);
        Assert.Null(result.ActiveMembersPerYear);
        Assert.Null(result.FirstYearsAtActivitiesPerYear);
    }

    [Fact]
    public async Task TotalMembers_CountsActiveStudentsAndSpecialMembersOnly()
    {
        var student = AddMember(D(2024, 9, 1));
        AddStudyEnrollment(student, _cs, D(2024, 9, 1));
        var graduated = AddMember(D(2020, 9, 1));
        AddStudyEnrollment(graduated, _cs, D(2020, 9, 1), StudyStatus.Completed, D(2023, 7, 1));
        AddMember(D(2010, 9, 1), ereLid: true);
        AddMember(D(2010, 9, 1), lidVanVerdienste: true);
        AddMember(D(2010, 9, 1), gratie: true);
        AddMember(D(2024, 9, 1), isDeleted: true);
        await _db.SaveChangesAsync();

        var result = await Get(StatisticType.TotalMembersPerYear);

        Assert.Equal(4, CurrentTotal(result));
    }

    [Fact]
    public async Task ActiveEnrollmentsPerStudy_CountsOnlyEnrolled()
    {
        var a = AddMember(D(2024, 9, 1));
        AddStudyEnrollment(a, _cs, D(2024, 9, 1));
        AddStudyEnrollment(a, _math, D(2024, 9, 1));
        var b = AddMember(D(2024, 9, 1));
        AddStudyEnrollment(b, _cs, D(2024, 9, 1));
        AddStudyEnrollment(b, _csMaster, D(2020, 9, 1), StudyStatus.DroppedOut, D(2021, 1, 1));
        await _db.SaveChangesAsync();

        var result = (await Get(StatisticType.ActiveEnrollmentsPerStudy)).ActiveEnrollmentsPerStudy!;

        Assert.Equal(2, result.Single(s => s.StudyId == _cs.Id).Count);
        Assert.Equal(1, result.Single(s => s.StudyId == _math.Id).Count);
        Assert.Equal(0, result.Single(s => s.StudyId == _csMaster.Id).Count);
        Assert.Equal(_cs.Id, result.First().StudyId);
    }

    [Fact]
    public async Task CurrentYear_DeletedAccountNeverCountsAsStudying()
    {
        var active = AddMember(D(2024, 9, 1));
        AddStudyEnrollment(active, _cs, D(2024, 9, 1));
        // An enrollment of a deleted account that was (wrongly) left open
        var deleted = AddMember(D(2024, 9, 1), isDeleted: true);
        AddStudyEnrollment(deleted, _cs, D(2024, 9, 1));
        await _db.SaveChangesAsync();

        var result = await Get(StatisticType.TotalMembersPerYear, StatisticType.ActiveEnrollmentsPerStudy);

        Assert.Equal(1, CurrentTotal(result));
        Assert.Equal(1, result.ActiveEnrollmentsPerStudy!.Single(s => s.StudyId == _cs.Id).Count);
    }

    [Fact]
    public async Task NewMembersPerYear_UsesMaxOfRegistrationAndFirstStudyYear_IncludingDeleted()
    {
        // Registered 2023/2024 (year 2024), first study 2024/2025 (year 2025) -> 2025
        var a = AddMember(D(2024, 3, 1));
        AddStudyEnrollment(a, _cs, D(2024, 9, 1));
        // Registered 2024/2025 (year 2025), first study long ago -> 2025
        var b = AddMember(D(2024, 10, 1), isDeleted: true);
        AddStudyEnrollment(b, _cs, D(2019, 9, 1), StudyStatus.Completed, D(2023, 7, 1));
        // No study but erelid / lid van verdienste, registered 2022/2023 -> 2023
        AddMember(D(2022, 12, 1), ereLid: true);
        AddMember(D(2023, 1, 1), lidVanVerdienste: true);
        // Gratie with a first study that started later -> year of that study (2024/2025 -> 2025)
        var gratie = AddMember(D(2023, 1, 1), gratie: true);
        AddStudyEnrollment(gratie, _cs, D(2024, 9, 1));
        // Never followed a study and no special status (incl. begunstiger): not a new member
        AddMember(D(2022, 12, 1));
        AddMember(D(2022, 12, 1), begunstiger: true);
        await _db.SaveChangesAsync();

        var result = (await Get(StatisticType.NewMembersPerYear)).NewMembersPerYear!;

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result.Single(r => r.Year == 2023).Count);
        Assert.Equal(3, result.Single(r => r.Year == 2025).Count);
    }

    [Fact]
    public async Task NewBachelorAndMasterMembersPerYear_UseTheStudyFollowedWhenBecomingAMember()
    {
        var bachelorThenMaster = AddMember(D(2021, 9, 1));               // new bachelor member in 2022; the later master doesn't count
        AddStudyEnrollment(bachelorThenMaster, _cs, D(2021, 9, 1), StudyStatus.Completed, D(2024, 7, 1));
        AddStudyEnrollment(bachelorThenMaster, _csMaster, D(2024, 9, 1));
        var masterOnly = AddMember(D(2024, 9, 1));                       // joins for a master -> new master member in 2025
        AddStudyEnrollment(masterOnly, _csMaster, D(2024, 9, 1));
        var joinedDuringMaster = AddMember(D(2024, 10, 1));              // bachelor elsewhere before, joined during the master -> master
        AddStudyEnrollment(joinedDuringMaster, _cs, D(2020, 9, 1), StudyStatus.Completed, D(2023, 7, 1));
        AddStudyEnrollment(joinedDuringMaster, _csMaster, D(2023, 9, 1));
        AddMember(D(2024, 9, 1), gratie: true);                          // no study -> neither
        await _db.SaveChangesAsync();

        var result = await Get(StatisticType.NewMembersPerYear, StatisticType.NewBachelorMembersPerYear, StatisticType.NewMasterMembersPerYear);

        var bachelor = Assert.Single(result.NewBachelorMembersPerYear!);
        Assert.Equal(2022u, bachelor.Year);
        Assert.Equal(1, bachelor.Count);

        var master = Assert.Single(result.NewMasterMembersPerYear!);
        Assert.Equal(2025u, master.Year);
        Assert.Equal(2, master.Count);

        // Both are a part of the new members of that year (3 in 2025, including the gratie member without a study).
        Assert.Equal(3, result.NewMembersPerYear!.Single(r => r.Year == 2025).Count);
    }

    [Fact]
    public async Task ActiveMembersPerYear_CountsUniqueMembersInAnyGroup_IncludingDeleted()
    {
        var g1 = AddGroup("Committee A");
        var g2 = AddGroup("Committee B");
        var a = AddMember(D(2023, 9, 1));
        var b = AddMember(D(2023, 9, 1), isDeleted: true);
        AddGroupMembership(a, g1, 2024);
        AddGroupMembership(a, g2, 2024);
        AddGroupMembership(b, g1, 2024);
        AddGroupMembership(a, g1, 2025);
        await _db.SaveChangesAsync();

        var result = (await Get(StatisticType.ActiveMembersPerYear)).ActiveMembersPerYear!;

        Assert.Equal(2, result.Single(r => r.Year == 2024).Count);
        Assert.Equal(1, result.Single(r => r.Year == 2025).Count);
    }

    [Fact]
    public async Task ActiveMembersPerStudy_UsesStudiesOverlappingThatYear()
    {
        var group = AddGroup("Committee");
        var a = AddMember(D(2021, 9, 1));
        AddStudyEnrollment(a, _cs, D(2021, 9, 1), StudyStatus.Completed, D(2024, 7, 15));
        AddStudyEnrollment(a, _csMaster, D(2024, 9, 1));
        var b = AddMember(D(2010, 9, 1), ereLid: true);
        AddGroupMembership(a, group, 2024); // 2023/2024: CS
        AddGroupMembership(a, group, 2025); // 2024/2025: CS Master
        AddGroupMembership(b, group, 2025); // no study
        await _db.SaveChangesAsync();

        var result = (await Get(StatisticType.ActiveMembersPerStudy)).ActiveMembersPerStudy!;

        var y2024 = result.Single(r => r.Year == 2024).Studies;
        Assert.Equal(_cs.Id, Assert.Single(y2024).StudyId);

        var y2025 = result.Single(r => r.Year == 2025).Studies;
        Assert.Equal(2, y2025.Count);
        Assert.Contains(y2025, s => s.StudyId == _csMaster.Id && s.Count == 1);
        Assert.Contains(y2025, s => s.StudyId == null && s.StudyTitle == null && s.Count == 1);
    }

    [Fact]
    public async Task FirstYearsAtActivitiesPerYear_CountsUniqueNewMembersAttendingActivities()
    {
        var past2025 = AddActivity(D(2024, 10, 1));
        var otherPast2025 = AddActivity(D(2024, 11, 1));
        var past2026 = AddActivity(D(2025, 10, 1));
        var future = AddActivity(DateTimeOffset.UtcNow.AddDays(30));

        // New in 2025 attending two activities: counted once
        var firstYear = AddMember(D(2024, 9, 1));
        AddStudyEnrollment(firstYear, _cs, D(2024, 9, 1));
        AddEnrollment(firstYear, past2025);
        AddEnrollment(firstYear, otherPast2025);
        // Same member in their second year: not new anymore
        AddEnrollment(firstYear, past2026);

        // Deleted new member: still counted
        var deleted = AddMember(D(2024, 9, 1), isDeleted: true);
        AddStudyEnrollment(deleted, _math, D(2024, 9, 1), StudyStatus.DroppedOut, D(2025, 1, 1));
        AddEnrollment(deleted, past2025);

        // New master member: part of the first-year intake as well
        var master = AddMember(D(2024, 9, 1));
        AddStudyEnrollment(master, _csMaster, D(2024, 9, 1));
        AddEnrollment(master, past2025);

        // On the waiting list: not counted
        var waiting = AddMember(D(2024, 9, 1));
        AddStudyEnrollment(waiting, _cs, D(2024, 9, 1));
        AddEnrollment(waiting, past2025, waitingList: true);

        // Member registered years ago, attending in 2025: not new in 2025
        var old = AddMember(D(2020, 9, 1));
        AddStudyEnrollment(old, _cs, D(2020, 9, 1));
        AddEnrollment(old, past2025);

        // Future activity: not attended yet
        var newFirstYear = AddMember(DateTimeOffset.UtcNow);
        AddStudyEnrollment(newFirstYear, _cs, DateTimeOffset.UtcNow);
        AddEnrollment(newFirstYear, future);
        await _db.SaveChangesAsync();

        var result = (await Get(StatisticType.FirstYearsAtActivitiesPerYear)).FirstYearsAtActivitiesPerYear!;

        var single = Assert.Single(result);
        Assert.Equal(2025u, single.Year);
        Assert.Equal(3, single.Count);
    }

    [Fact]
    public async Task ActiveMembers_ExcludeDisputes()
    {
        var committee = AddGroup("Committee");
        var dispute = AddGroup("Dispuut", GroupType.Dispute);
        var both = AddMember(D(2023, 9, 1));
        AddStudyEnrollment(both, _cs, D(2023, 9, 1));
        var onlyDispute = AddMember(D(2023, 9, 1));
        AddStudyEnrollment(onlyDispute, _math, D(2023, 9, 1));
        AddGroupMembership(both, committee, 2024);
        AddGroupMembership(both, dispute, 2024);
        AddGroupMembership(onlyDispute, dispute, 2024);
        await _db.SaveChangesAsync();

        var result = await Get(StatisticType.ActiveMembersPerYear, StatisticType.ActiveMembersPerStudy);

        Assert.Equal(1, Assert.Single(result.ActiveMembersPerYear!).Count);
        var studies = Assert.Single(result.ActiveMembersPerStudy!).Studies;
        Assert.Equal(_cs.Id, Assert.Single(studies).StudyId);
    }

    [Fact]
    public async Task GetStatistics_DefaultsToCurrentYearSnapshot()
    {
        var before = DateTimeOffset.UtcNow;

        var result = await Get(StatisticType.ActiveEnrollmentsPerStudy);

        Assert.Equal(YearUtils.GetYearForDate(DateTime.UtcNow, "08-01"), result.Year);
        Assert.True(result.SnapshotAt >= before);
    }

    [Fact]
    public async Task GetStatistics_FutureYear_Throws()
    {
        var future = YearUtils.GetYearForDate(DateTime.UtcNow, "08-01") + 1;

        await Assert.ThrowsAsync<ArgumentException>(() => GetForYear(future, StatisticType.ActiveEnrollmentsPerStudy));
    }

    [Fact]
    public async Task PastYear_UsesStateAtEndOfThatYear()
    {
        // Year 2023 = 2022/2023, so the snapshot is at 2023-08-01.
        var graduated = AddMember(D(2020, 9, 1)); // studied CS 2020 - 2024: member then, not now
        AddStudyEnrollment(graduated, _cs, D(2020, 9, 1), StudyStatus.Completed, D(2024, 7, 1));
        var deleted = AddMember(D(2021, 9, 1), isDeleted: true); // dropped out after the snapshot: member then
        AddStudyEnrollment(deleted, _math, D(2021, 9, 1), StudyStatus.DroppedOut, D(2024, 1, 1));
        AddStudyEnrollment(deleted, _cs, D(2021, 9, 1), StudyStatus.DroppedOut, D(2024, 1, 1));
        var droppedBefore = AddMember(D(2019, 9, 1)); // dropped out before the snapshot
        AddStudyEnrollment(droppedBefore, _cs, D(2019, 9, 1), StudyStatus.DroppedOut, D(2022, 1, 1));
        var later = AddMember(D(2024, 9, 1)); // registered after the snapshot
        AddStudyEnrollment(later, _cs, D(2024, 9, 1));
        var ongoing = AddMember(D(2018, 9, 1)); // still studying
        AddStudyEnrollment(ongoing, _csMaster, D(2018, 9, 1));
        await _db.SaveChangesAsync();

        var past = await GetForYear(2023, StatisticType.ActiveEnrollmentsPerStudy);

        Assert.Equal(2023u, past.Year);
        Assert.Equal(new DateTimeOffset(2023, 7, 31, 22, 0, 0, TimeSpan.Zero), past.SnapshotAt);
        Assert.Equal(2, past.ActiveEnrollmentsPerStudy!.Single(s => s.StudyId == _cs.Id).Count);
        Assert.Equal(1, past.ActiveEnrollmentsPerStudy!.Single(s => s.StudyId == _math.Id).Count);
        Assert.Equal(1, past.ActiveEnrollmentsPerStudy!.Single(s => s.StudyId == _csMaster.Id).Count);

        var totals = (await Get(StatisticType.TotalMembersPerYear)).TotalMembersPerYear!;
        Assert.Equal(3, totals.Single(t => t.Year == 2023).Count); // graduated, deleted, ongoing at the end of 2022/2023
        Assert.Equal(2, totals.Last().Count); // now: later + ongoing
    }

    [Fact]
    public async Task NewMembersPerStudy_UsesTheStudyFollowedWhenCountedAsNew()
    {
        // All new in 2024/2025 (year 2025)
        var registeredDuringStudy = AddMember(D(2024, 10, 1));            // studying CS since 2023 -> CS
        AddStudyEnrollment(registeredDuringStudy, _cs, D(2023, 9, 1));
        AddStudyEnrollment(registeredDuringStudy, _csMaster, D(2026, 9, 1)); // later study, not followed then
        var studyStartsLater = AddMember(D(2024, 3, 1));                  // first study starts 2024-09 -> Math
        AddStudyEnrollment(studyStartsLater, _math, D(2024, 9, 1));
        var twoStudies = AddMember(D(2024, 9, 1));                        // CS + Math at the same time -> both
        AddStudyEnrollment(twoStudies, _cs, D(2024, 9, 1));
        AddStudyEnrollment(twoStudies, _math, D(2024, 9, 1));
        var graduatedBefore = AddMember(D(2024, 10, 1));                  // finished before joining -> last started study
        AddStudyEnrollment(graduatedBefore, _cs, D(2018, 9, 1), StudyStatus.Completed, D(2021, 7, 1));
        AddStudyEnrollment(graduatedBefore, _csMaster, D(2021, 9, 1), StudyStatus.Completed, D(2023, 7, 1));
        AddMember(D(2024, 10, 1), ereLid: true);                          // no study -> no-study bucket
        // New in another year: not counted for 2025
        var other = AddMember(D(2020, 9, 1));
        AddStudyEnrollment(other, _cs, D(2020, 9, 1));
        await _db.SaveChangesAsync();

        var result = (await Get(StatisticType.NewMembersPerStudy)).NewMembersPerStudy!;

        var y2025 = result.Single(r => r.Year == 2025).Studies;
        Assert.Equal(2, y2025.Single(s => s.StudyId == _cs.Id).Count);
        Assert.Equal(2, y2025.Single(s => s.StudyId == _math.Id).Count);
        Assert.Equal(1, y2025.Single(s => s.StudyId == _csMaster.Id).Count);
        Assert.Equal(1, y2025.Single(s => s.StudyId == null).Count);
        Assert.Equal(1, result.Single(r => r.Year == 2021).Studies.Single().Count);
    }

    [Fact]
    public async Task NewActiveMembersPerStudy_OnlyCountsNewMembersInANonDisputeGroupThatYear()
    {
        var committee = AddGroup("Commissie");
        var dispute = AddGroup("Dispuut", GroupType.Dispute);
        // All new in 2024/2025 (year 2025)
        var activeCs = AddMember(D(2024, 9, 1));                           // committee in 2025 -> counted for CS
        AddStudyEnrollment(activeCs, _cs, D(2024, 9, 1));
        AddGroupMembership(activeCs, committee, 2025);
        var activeTwoStudies = AddMember(D(2024, 9, 1));                   // CS + Math -> counted for both
        AddStudyEnrollment(activeTwoStudies, _cs, D(2024, 9, 1));
        AddStudyEnrollment(activeTwoStudies, _math, D(2024, 9, 1));
        AddGroupMembership(activeTwoStudies, committee, 2025);
        var disputeOnly = AddMember(D(2024, 9, 1));                        // only a dispute -> not active
        AddStudyEnrollment(disputeOnly, _cs, D(2024, 9, 1));
        AddGroupMembership(disputeOnly, dispute, 2025);
        var activeLater = AddMember(D(2024, 9, 1));                        // active only the next year -> not counted
        AddStudyEnrollment(activeLater, _math, D(2024, 9, 1));
        AddGroupMembership(activeLater, committee, 2026);
        var inactive = AddMember(D(2024, 9, 1));                           // no group -> not counted
        AddStudyEnrollment(inactive, _cs, D(2024, 9, 1));
        // Active in 2025 but new in an earlier year -> not counted
        var oldMember = AddMember(D(2020, 9, 1));
        AddStudyEnrollment(oldMember, _cs, D(2020, 9, 1));
        AddGroupMembership(oldMember, committee, 2025);
        await _db.SaveChangesAsync();

        var result = await Get(StatisticType.NewMembersPerStudy, StatisticType.NewActiveMembersPerStudy);

        var active2025 = Assert.Single(result.NewActiveMembersPerStudy!).Studies;
        Assert.Equal(2025u, result.NewActiveMembersPerStudy!.Single().Year);
        Assert.Equal(2, active2025.Single(s => s.StudyId == _cs.Id).Count);
        Assert.Equal(1, active2025.Single(s => s.StudyId == _math.Id).Count);
        // The new members of that year are all still counted in NewMembersPerStudy.
        var new2025 = result.NewMembersPerStudy!.Single(r => r.Year == 2025).Studies;
        Assert.Equal(4, new2025.Single(s => s.StudyId == _cs.Id).Count);
        Assert.Equal(2, new2025.Single(s => s.StudyId == _math.Id).Count);
    }

    [Fact]
    public async Task NewMembers_WithoutRegistrationDate_CountInYearOfFirstStudy()
    {
        // Accounts created outside the registration flow can have no real registration date (-infinity in Postgres).
        var member = AddMember(DateTimeOffset.MinValue);
        AddStudyEnrollment(member, _cs, D(2026, 9, 17));
        await _db.SaveChangesAsync();

        var result = await Get(StatisticType.NewMembersPerYear, StatisticType.NewMembersPerStudy);

        var year = Assert.Single(result.NewMembersPerYear!);
        Assert.Equal(2027u, year.Year);
        Assert.Equal(1, year.Count);
        Assert.Equal(_cs.Id, Assert.Single(Assert.Single(result.NewMembersPerStudy!).Studies).StudyId);
    }

    [Fact]
    public async Task TotalMembersPerYear_RunsFromFirstNewMemberYearToNow()
    {
        var currentYear = YearUtils.GetYearForDate(DateTime.UtcNow, "08-01");
        var old = AddMember(D(2019, 9, 1)); // member from 2019/2020 (year 2020), graduates during 2022/2023
        AddStudyEnrollment(old, _cs, D(2019, 9, 1), StudyStatus.Completed, D(2022, 12, 1));
        var current = AddMember(D(2021, 9, 1)); // member since 2021/2022 (year 2022), still studying
        AddStudyEnrollment(current, _math, D(2021, 9, 1));
        AddMember(DateTimeOffset.MinValue); // no registration date and no study: not a member, must not stretch the series
        await _db.SaveChangesAsync();

        var result = (await Get(StatisticType.TotalMembersPerYear)).TotalMembersPerYear!;

        Assert.Equal(2020u, result.First().Year);
        Assert.Equal(currentYear, result.Last().Year);
        Assert.Equal((int)(currentYear - 2020 + 1), result.Count);
        Assert.Equal(1, result.Single(r => r.Year == 2020).Count);
        Assert.Equal(1, result.Single(r => r.Year == 2021).Count);
        Assert.Equal(2, result.Single(r => r.Year == 2022).Count); // end of 2021/2022: both
        Assert.Equal(1, result.Single(r => r.Year == 2023).Count); // end of 2022/2023: old has graduated
        Assert.Equal(1, result.Last().Count);
    }

    [Fact]
    public async Task ActivitiesPerInvolvedMember_CountsAttendedActivitiesPerMemberPerYear()
    {
        var a1 = AddActivity(D(2024, 10, 1));
        var a2 = AddActivity(D(2024, 11, 1));
        var a3 = AddActivity(D(2025, 2, 1));
        var nextYear = AddActivity(D(2025, 10, 1));
        var future = AddActivity(DateTimeOffset.UtcNow.AddDays(30));

        var three = AddMember(D(2023, 9, 1));
        AddEnrollment(three, a1);
        AddEnrollment(three, a2);
        AddEnrollment(three, a3);
        AddEnrollment(three, nextYear); // other year
        var oneA = AddMember(D(2023, 9, 1));
        AddEnrollment(oneA, a1);
        AddEnrollment(oneA, a2, waitingList: true); // waiting list doesn't count
        var oneB = AddMember(D(2023, 9, 1), isDeleted: true);
        AddEnrollment(oneB, a3);
        var none = AddMember(D(2023, 9, 1));
        AddEnrollment(none, future); // hasn't happened yet: not involved
        await _db.SaveChangesAsync();

        var result = (await Get(StatisticType.ActivitiesPerInvolvedMember)).ActivitiesPerInvolvedMember!;

        var y2025 = result.Single(r => r.Year == 2025).Distribution;
        Assert.Equal(2, y2025.Count);
        Assert.Equal(2, y2025.Single(b => b.Activities == 1).Members);
        Assert.Equal(1, y2025.Single(b => b.Activities == 3).Members);
        var y2026 = Assert.Single(result.Single(r => r.Year == 2026).Distribution);
        Assert.Equal(1, y2026.Activities);
        Assert.Equal(1, y2026.Members);
        Assert.DoesNotContain(result, r => r.Distribution.Any(b => b.Activities == 0));
    }

    [Fact]
    public async Task GetStatistics_AllTypes_PopulatesEverything()
    {
        var result = await Get(Enum.GetValues<StatisticType>());

        Assert.NotNull(result.TotalMembersPerYear);
        Assert.NotNull(result.ActivitiesPerInvolvedMember);
        Assert.NotNull(result.ActiveEnrollmentsPerStudy);
        Assert.NotNull(result.NewMembersPerYear);
        Assert.NotNull(result.NewBachelorMembersPerYear);
        Assert.NotNull(result.NewMasterMembersPerYear);
        Assert.NotNull(result.ActiveMembersPerYear);
        Assert.NotNull(result.ActiveMembersPerStudy);
        Assert.NotNull(result.NewMembersPerStudy);
        Assert.NotNull(result.FirstYearsAtActivitiesPerYear);
        Assert.NotNull(result.NewActiveMembersPerStudy);
    }
}
