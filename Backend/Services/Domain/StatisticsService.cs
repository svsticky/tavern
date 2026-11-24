using Backend.Controllers.DTOs;
using Backend.Database;
using Backend.Interfaces;
using Backend.Models.Domain;
using Backend.Utils.DateTime;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services.Domain;

/// <summary>
/// Computes association-wide statistics for the administrative statistics page.
/// <para>
/// All "per year" statistics use the committee year, i.e. <see cref="YearUtils.GetYearForDate"/> with
/// <see cref="YearUtils.CommitteeCreationDate"/> as the start date. Deleted (soft-deleted, anonymized) members are
/// explicitly included by bypassing the global <c>IsDeleted</c> query filter, so historical numbers do not shrink
/// when an account is removed.
/// </para>
/// </summary>
public class StatisticsService(
    PostgresDbContext db,
    IPermissionService permissionService,
    ILogger<StatisticsService> logger) : IStatisticsService
{
    private sealed record MemberRow(Guid Id, DateTimeOffset RegisteredOn, bool EreLid, bool LidVanVerdienste, bool Gratie, bool IsDeleted);
    private sealed record StudyEnrollmentRow(Guid MemberId, uint StudyId, DateTimeOffset EnrollmentDate, DateTimeOffset? CompletionDate, StudyStatus Status);
    private sealed record GroupMembershipRow(Guid MemberId, uint MembershipYear);
    private sealed record AttendanceRow(Guid MemberId, DateTimeOffset ActivityStart);

    /// <summary>
    /// The moment the point-in-time statistics are computed for. For the current committee year this is "now" and the
    /// live state is used (status Enrolled, deleted accounts excluded). For a past year it is the end of that year: a
    /// study enrollment counts when it started before and ended after that moment (or is still ongoing), and a member
    /// counts when they had registered by then; deleted accounts are included because they may have been members then.
    /// The honorary/merit/gratie flags have no history, so their current value is used.
    /// </summary>
    private sealed record Snapshot(DateTimeOffset At, bool IsCurrent)
    {
        public bool IsActive(StudyEnrollmentRow se) => IsCurrent
            ? se.Status == StudyStatus.Enrolled
            : se.EnrollmentDate <= At && (se.CompletionDate == null ? se.Status == StudyStatus.Enrolled : se.CompletionDate > At);

        public bool Counts(MemberRow m) => IsCurrent ? !m.IsDeleted : m.RegisteredOn <= At;
    }

    /// <summary>
    /// Lazily loaded, per-request cache of the datasets the statistics are computed from, so that requesting all
    /// statistics hits each table only once, while requesting a single statistic only loads what it needs.
    /// </summary>
    private sealed class DataCache(PostgresDbContext db, CancellationToken ct)
    {
        private List<MemberRow>? _members;
        private List<StudyEnrollmentRow>? _studyEnrollments;
        private Dictionary<uint, Study>? _studies;
        private List<GroupMembershipRow>? _groupMemberships;
        private List<AttendanceRow>? _attendances;

        public async Task<List<MemberRow>> Members() => _members ??= await db.Members
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(m => new MemberRow(m.Id, m.RegisteredOn, m.EreLid, m.LidVanVerdienste, m.Gratie, m.IsDeleted))
            .ToListAsync(ct);

        public async Task<List<StudyEnrollmentRow>> StudyEnrollments() => _studyEnrollments ??= await db.StudyEnrollments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(se => new StudyEnrollmentRow(se.MemberId, se.StudyId, se.EnrollmentDate, se.CompletionDate, se.Status))
            .ToListAsync(ct);

        public async Task<Dictionary<uint, Study>> Studies() => _studies ??= await db.Studies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .ToDictionaryAsync(s => s.Id, ct);

        /// <summary>Group memberships that make someone an active member: disputes (GroupType.Dispute) are excluded.</summary>
        public async Task<List<GroupMembershipRow>> GroupMemberships() => _groupMemberships ??= await db.GroupMemberships
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(gm => gm.Group.Type != GroupType.Dispute)
            .Select(gm => new GroupMembershipRow(gm.MemberId, gm.MembershipYear))
            .Distinct()
            .ToListAsync(ct);

        /// <summary>Confirmed (non-waiting-list) enrollments for activities that already started.</summary>
        public async Task<List<AttendanceRow>> Attendances()
        {
            if (_attendances != null)
                return _attendances;

            var now = DateTimeOffset.UtcNow;
            return _attendances = await db.Enrollments
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(e => !e.IsOnWaitingList && e.Activity.DateTimeStart <= now)
                .Select(e => new AttendanceRow(e.MemberId, e.Activity.DateTimeStart))
                .ToListAsync(ct);
        }
    }

    /// <inheritdoc />
    public async Task<StatisticsResponseDTO> GetStatistics(IReadOnlySet<StatisticType> statistics, uint? year, Guid userId, CancellationToken ct)
    {
        permissionService.EnsureBoardOrCandidateBoardMember(userId);
        logger.LogInformation("Computing statistics {Statistics} for year {Year} for user {UserId}.", string.Join(", ", statistics), year, userId);

        var now = DateTimeOffset.UtcNow;
        var currentYear = CommitteeYear(now);
        var snapshotYear = year ?? currentYear;
        if (snapshotYear > currentYear)
            throw new ArgumentException($"Year {snapshotYear} lies in the future; the current committee year is {currentYear}.");

        var snapshot = SnapshotFor(snapshotYear, currentYear, now);

        var data = new DataCache(db, ct);
        var result = new StatisticsResponseDTO { Year = snapshotYear, SnapshotAt = snapshot.At };

        if (statistics.Contains(StatisticType.TotalMembersPerYear))
            result.TotalMembersPerYear = await GetTotalMembersPerYear(data, currentYear, now);

        if (statistics.Contains(StatisticType.ActiveEnrollmentsPerStudy))
            result.ActiveEnrollmentsPerStudy = await GetActiveEnrollmentsPerStudy(data, snapshot);

        if (statistics.Contains(StatisticType.NewMembersPerYear))
            result.NewMembersPerYear = await GetNewMembersPerYear(data, null);

        if (statistics.Contains(StatisticType.NewBachelorMembersPerYear))
            result.NewBachelorMembersPerYear = await GetNewMembersPerYear(data, StudyType.Bachelor);

        if (statistics.Contains(StatisticType.NewMasterMembersPerYear))
            result.NewMasterMembersPerYear = await GetNewMembersPerYear(data, StudyType.Master);

        if (statistics.Contains(StatisticType.ActiveMembersPerYear))
            result.ActiveMembersPerYear = await GetActiveMembersPerYear(data);

        if (statistics.Contains(StatisticType.ActiveMembersPerStudy))
            result.ActiveMembersPerStudy = await GetActiveMembersPerStudy(data);

        if (statistics.Contains(StatisticType.ActivitiesPerInvolvedMember))
            result.ActivitiesPerInvolvedMember = await GetActivitiesPerInvolvedMember(data);

        if (statistics.Contains(StatisticType.NewMembersPerStudy))
            result.NewMembersPerStudy = await GetNewMembersPerStudy(data, onlyActive: false);

        if (statistics.Contains(StatisticType.NewActiveMembersPerStudy))
            result.NewActiveMembersPerStudy = await GetNewMembersPerStudy(data, onlyActive: true);

        if (statistics.Contains(StatisticType.FirstYearsAtActivitiesPerYear))
            result.FirstYearsAtActivitiesPerYear = await GetFirstYearsAtActivitiesPerYear(data);

        return result;
    }

    private static uint CommitteeYear(DateTimeOffset date)
        => YearUtils.GetYearForDate(date.UtcDateTime, YearUtils.CommitteeCreationDate);

    /// <summary>The snapshot for a committee year: now for the current year, otherwise the end of that year.</summary>
    private static Snapshot SnapshotFor(uint year, uint currentYear, DateTimeOffset now)
        => year == currentYear
            ? new Snapshot(now, IsCurrent: true)
            : new Snapshot(YearUtils.GetYearStartUtc(year + 1, YearUtils.CommitteeCreationDate), IsCurrent: false);

    /// <summary>
    /// The number of members (see <see cref="GetTotalMembers"/>) at the end of every committee year, and now for the
    /// current year. The series starts at the first year anyone became a member (see <see cref="GetNewMemberYears"/>),
    /// so accounts without a real registration date don't stretch it back to year 1.
    /// </summary>
    private static async Task<List<YearCountDTO>> GetTotalMembersPerYear(DataCache data, uint currentYear, DateTimeOffset now)
    {
        var newMemberYears = (await GetNewMemberYears(data)).Values;
        var firstYear = newMemberYears.Count > 0 ? Math.Min(newMemberYears.Min(), currentYear) : currentYear;

        var result = new List<YearCountDTO>();
        for (var year = firstYear; year <= currentYear; year++)
            result.Add(new YearCountDTO { Year = year, Count = await GetTotalMembers(data, SnapshotFor(year, currentYear, now)) });
        return result;
    }

    private static List<YearCountDTO> ToYearCounts(IEnumerable<uint> years)
        => years
            .GroupBy(y => y)
            .OrderBy(g => g.Key)
            .Select(g => new YearCountDTO { Year = g.Key, Count = g.Count() })
            .ToList();

    /// <summary>
    /// Counts members that have an active study at the snapshot moment, or are an honorary member, "Lid van Verdienste" or
    /// gratie member (see <see cref="Snapshot"/>).
    /// Deleted members have their flags cleared and their studies closed or removed, so they only qualify for past years.
    /// </summary>
    private static async Task<int> GetTotalMembers(DataCache data, Snapshot snapshot)
    {
        var activeStudents = (await data.StudyEnrollments())
            .Where(snapshot.IsActive)
            .Select(se => se.MemberId)
            .ToHashSet();

        return (await data.Members()).Count(m =>
            snapshot.Counts(m) &&
            (activeStudents.Contains(m.Id) || m.EreLid || m.LidVanVerdienste || m.Gratie));
    }

    /// <summary>
    /// Counts the study enrollments that are active at the snapshot moment, of members that count at that moment
    /// (so a deleted account never counts as currently studying, even if an enrollment of it was left open).
    /// </summary>
    private static async Task<List<StudyCountDTO>> GetActiveEnrollmentsPerStudy(DataCache data, Snapshot snapshot)
    {
        var countingMembers = (await data.Members()).Where(snapshot.Counts).Select(m => m.Id).ToHashSet();
        var counts = (await data.StudyEnrollments())
            .Where(se => snapshot.IsActive(se) && countingMembers.Contains(se.MemberId))
            .GroupBy(se => se.StudyId)
            .ToDictionary(g => g.Key, g => g.Count());

        return (await data.Studies()).Values
            .Where(s => s.Active || counts.ContainsKey(s.Id))
            .Select(s => new StudyCountDTO
            {
                StudyId = s.Id,
                StudyTitle = s.Title,
                StudyType = s.Type,
                Count = counts.GetValueOrDefault(s.Id),
            })
            .OrderByDescending(s => s.Count)
            .ThenBy(s => s.StudyTitle)
            .ToList();
    }

    /// <summary>
    /// The committee year in which each member counts as new: the year they registered, unless their first study
    /// starts later, then the year that study starts (max of both). Only members that ever followed a study, or are a
    /// gratie member, honorary member or "Lid van Verdienste", count as new members at all ("begunstigers" are not
    /// members). The same year rule applies to all of them.
    /// </summary>
    private static async Task<Dictionary<Guid, uint>> GetNewMemberYears(DataCache data)
        => (await GetNewMemberMoments(data)).ToDictionary(kv => kv.Key, kv => CommitteeYear(kv.Value));

    /// <summary>
    /// The moment each member counts as new (see <see cref="GetNewMemberYears"/>): their registration, or the start of
    /// their first study when that is later.
    /// </summary>
    private static async Task<Dictionary<Guid, DateTimeOffset>> GetNewMemberMoments(DataCache data)
    {
        var firstEnrollmentByMember = (await data.StudyEnrollments())
            .GroupBy(se => se.MemberId)
            .ToDictionary(g => g.Key, g => g.Min(se => se.EnrollmentDate));

        var moments = new Dictionary<Guid, DateTimeOffset>();
        foreach (var member in await data.Members())
        {
            var hasEnrollment = firstEnrollmentByMember.TryGetValue(member.Id, out var firstEnrollment);
            if (!hasEnrollment && !(member.Gratie || member.EreLid || member.LidVanVerdienste))
                continue;

            moments[member.Id] = hasEnrollment && firstEnrollment > member.RegisteredOn ? firstEnrollment : member.RegisteredOn;
        }

        return moments;
    }

    /// <summary>
    /// For every committee year, counts that year's new members (see <see cref="GetNewMemberYears"/>) per study they
    /// followed at the moment they counted as new: the studies running at that moment, or else the study they started
    /// most recently before it. A member with multiple such studies counts once for each; members without any study
    /// (honorary, merit or gratie members) are counted in a bucket without a study.
    /// </summary>
    /// <param name="data">The data the statistics are calculated from.</param>
    /// <param name="onlyActive">Only count the new members that were also active in the year they were new: in at least
    /// one non-dispute group that year.</param>
    private static async Task<List<YearStudyCountsDTO>> GetNewMembersPerStudy(DataCache data, bool onlyActive)
    {
        var studies = await data.Studies();
        var enrollmentsByMember = (await data.StudyEnrollments()).ToLookup(se => se.MemberId);
        var activeInYear = onlyActive
            ? (await data.GroupMemberships()).Select(gm => (gm.MembershipYear, gm.MemberId)).ToHashSet()
            : null;

        return (await GetNewMemberMoments(data))
            .Where(kv => activeInYear == null || activeInYear.Contains((CommitteeYear(kv.Value), kv.Key)))
            .GroupBy(kv => CommitteeYear(kv.Value))
            .OrderBy(g => g.Key)
            .Select(yearGroup => new YearStudyCountsDTO
            {
                Year = yearGroup.Key,
                Studies = ToStudyCounts(studies, yearGroup.SelectMany(kv =>
                {
                    var ids = StudiesFollowedAt(enrollmentsByMember[kv.Key], kv.Value).Select(id => (uint?)id).ToList();
                    return ids.Count > 0 ? ids : [null];
                })),
            })
            .ToList();
    }

    /// <summary>
    /// The studies a member followed at a moment: the studies running then, or else the study they started most
    /// recently before it. Empty when they had not started any study yet.
    /// </summary>
    private static List<uint> StudiesFollowedAt(IEnumerable<StudyEnrollmentRow> enrollments, DateTimeOffset moment)
    {
        var startedBefore = enrollments.Where(se => se.EnrollmentDate <= moment).ToList();
        var running = startedBefore.Where(se => se.CompletionDate == null || se.CompletionDate > moment).ToList();
        var followed = running.Count > 0
            ? running
            : startedBefore.Where(se => se.EnrollmentDate == startedBefore.Max(x => x.EnrollmentDate)).ToList();
        return followed.Select(se => se.StudyId).Distinct().ToList();
    }

    /// <summary>Groups study ids (null = no study) into per-study counts, largest first.</summary>
    private static List<StudyCountDTO> ToStudyCounts(IReadOnlyDictionary<uint, Study> studies, IEnumerable<uint?> studyIds)
        => studyIds
            .GroupBy(id => id)
            .Select(g =>
            {
                var study = g.Key.HasValue ? studies.GetValueOrDefault(g.Key.Value) : null;
                return new StudyCountDTO
                {
                    StudyId = g.Key,
                    StudyTitle = study?.Title,
                    StudyType = study?.Type,
                    Count = g.Count(),
                };
            })
            .OrderByDescending(s => s.Count)
            .ThenBy(s => s.StudyTitle)
            .ToList();

    /// <summary>
    /// The new members per committee year (see <see cref="GetNewMemberYears"/>). With a study type, only the part of
    /// them that became a member while following a study of that type (see <see cref="StudiesFollowedAt"/>): a member
    /// who later starts a master is not a new master member, because they were not new anymore.
    /// </summary>
    private static async Task<List<YearCountDTO>> GetNewMembersPerYear(DataCache data, StudyType? studyType)
    {
        var moments = await GetNewMemberMoments(data);
        if (studyType == null)
            return ToYearCounts(moments.Values.Select(CommitteeYear));

        var studies = await data.Studies();
        var enrollmentsByMember = (await data.StudyEnrollments()).ToLookup(se => se.MemberId);
        return ToYearCounts(moments
            .Where(kv => StudiesFollowedAt(enrollmentsByMember[kv.Key], kv.Value)
                .Any(id => studies.TryGetValue(id, out var study) && study.Type == studyType))
            .Select(kv => CommitteeYear(kv.Value)));
    }

    private static async Task<List<YearCountDTO>> GetActiveMembersPerYear(DataCache data)
        => ToYearCounts((await data.GroupMemberships()).Select(gm => gm.MembershipYear));

    /// <summary>The active members per committee year: members in at least one non-dispute group that year.</summary>
    private static async Task<List<YearStudyCountsDTO>> GetActiveMembersPerStudy(DataCache data)
        => await GetMembersPerStudy(data, (await data.GroupMemberships()).Select(gm => (gm.MembershipYear, gm.MemberId)));

    /// <summary>
    /// For every committee year, how many members attended exactly 1, 2, 3, ... activities that started in that year
    /// (see <see cref="DataCache.Attendances"/>). Members that attended nothing don't appear in the attendances, so
    /// they are not part of the distribution.
    /// </summary>
    private static async Task<List<YearActivityDistributionDTO>> GetActivitiesPerInvolvedMember(DataCache data)
        => (await data.Attendances())
            .GroupBy(a => (Year: CommitteeYear(a.ActivityStart), a.MemberId))
            .Select(g => (g.Key.Year, Activities: g.Count()))
            .GroupBy(m => m.Year, m => m.Activities)
            .OrderBy(g => g.Key)
            .Select(g => new YearActivityDistributionDTO
            {
                Year = g.Key,
                Distribution = g
                    .GroupBy(activities => activities)
                    .OrderBy(b => b.Key)
                    .Select(b => new ActivityCountDTO { Activities = b.Key, Members = b.Count() })
                    .ToList(),
            })
            .ToList();

    /// <summary>
    /// For every committee year, counts the given members of that year per study they were enrolled in during that year
    /// (an enrollment counts when it overlaps the year). A member following multiple studies counts once for each of
    /// them; members without a study in that year are counted in a bucket without a study.
    /// </summary>
    private static async Task<List<YearStudyCountsDTO>> GetMembersPerStudy(DataCache data, IEnumerable<(uint Year, Guid MemberId)> membersPerYear)
    {
        var studies = await data.Studies();
        var enrollmentsByMember = (await data.StudyEnrollments()).ToLookup(se => se.MemberId);

        return membersPerYear
            .GroupBy(m => m.Year, m => m.MemberId)
            .OrderBy(g => g.Key)
            .Select(yearGroup =>
            {
                var yearStart = YearUtils.GetYearStartUtc(yearGroup.Key, YearUtils.CommitteeCreationDate);
                var yearEnd = YearUtils.GetYearStartUtc(yearGroup.Key + 1, YearUtils.CommitteeCreationDate);

                var studyIds = yearGroup
                    .Distinct()
                    .SelectMany(memberId =>
                    {
                        var ids = enrollmentsByMember[memberId]
                            .Where(se => se.EnrollmentDate < yearEnd && (se.CompletionDate == null || se.CompletionDate >= yearStart))
                            .Select(se => (uint?)se.StudyId)
                            .Distinct()
                            .ToList();
                        return ids.Count > 0 ? ids : [null];
                    });

                return new YearStudyCountsDTO { Year = yearGroup.Key, Studies = ToStudyCounts(studies, studyIds) };
            })
            .ToList();
    }

    /// <summary>
    /// The first-years ("eerstejaarslichting") of a committee year are all members that are new in that year
    /// (see <see cref="GetNewMemberYears"/>, bachelor and master alike). They are counted for that year when they
    /// attended at least one activity (see <see cref="DataCache.Attendances"/>) that started in that same year.
    /// This matches the definition used in the board's annual report.
    /// </summary>
    private static async Task<List<YearCountDTO>> GetFirstYearsAtActivitiesPerYear(DataCache data)
    {
        var newMemberYears = await GetNewMemberYears(data);

        var years = (await data.Attendances())
            .Select(a => (a.MemberId, Year: CommitteeYear(a.ActivityStart)))
            .Where(a => newMemberYears.TryGetValue(a.MemberId, out var newYear) && newYear == a.Year)
            .Distinct()
            .Select(a => a.Year);

        return ToYearCounts(years);
    }
}
