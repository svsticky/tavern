using Backend.Database;
using Backend.Models.Domain;
using Backend.Services.DirectoryServices;
using Backend.Services.OutboxWorkers;
using Backend.Utils.DateTime;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace Backend.Tests.Services;

public class DirectoryMailChangedListenerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<PostgresDbContext> _dbOptions;
    private readonly PostgresDbContext _db;
    private readonly DirectoryOutboxWorker _outboxWorker;
    private readonly DirectoryMailChangedListener _listener;

    public DirectoryMailChangedListenerTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        _dbOptions = new DbContextOptionsBuilder<PostgresDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new PostgresDbContext(_dbOptions);
        _db.Database.EnsureCreated();

        _outboxWorker = Substitute.For<DirectoryOutboxWorker>(null, null);
        _outboxWorker.When(x => x.EnqueueMigrateMemberEmailForActiveGroupsTask(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<PostgresDbContext>()))
            .CallBase();
        _listener = new DirectoryMailChangedListener(_outboxWorker);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public void OnMailChanged_DoesNothing_WhenEmailIsSame()
    {
        _listener.OnMailChanged(Guid.NewGuid(), "same@svsticky.nl", "same@svsticky.nl", _db);

        _outboxWorker.DidNotReceiveWithAnyArgs().EnqueueMigrateMemberEmailTask(default!, default!, default!, default!);
    }

    [Fact]
    public void OnMailChanged_EnqueuesMigrateTask_ForActiveGroupMemberships()
    {
        var memberId = Guid.NewGuid();
        var currentYear = YearUtils.GetCommitteeYear();

        var group1 = new Group { Id = 10, Name = "Webcie", Active = true, DirectoryGroupEmail = "webcie@svsticky.nl" };
        var group2 = new Group { Id = 20, Name = "Social", Active = true, DirectoryGroupEmail = null }; // no directory email
        var group3 = new Group { Id = 30, Name = "Bar", Active = true, DirectoryGroupEmail = "bar@svsticky.nl" };

        _db.Groups.AddRange(group1, group2, group3);

        var member = new Member
        {
            Id = memberId,
            FirstName = "Alice",
            LastName = "Smith",
            Email = "new@svsticky.nl",
            StudentNumber = "1234567",
            PhoneNumber = "0612345678",
            Street = "Heidelberglaan",
            HouseNumber = "8",
            PostalCode = "3584CS",
            City = "Utrecht"
        };
        _db.Members.Add(member);

        _db.GroupMemberships.AddRange(
            new GroupMembership { GroupId = group1.Id, MemberId = memberId, MembershipYear = currentYear },
            new GroupMembership { GroupId = group2.Id, MemberId = memberId, MembershipYear = currentYear },
            new GroupMembership { GroupId = group3.Id, MemberId = memberId, MembershipYear = currentYear - 1 } // past year, should be ignored
        );
        _db.SaveChanges();

        _listener.OnMailChanged(memberId, "old@svsticky.nl", "new@svsticky.nl", _db);

        _outboxWorker.Received(1).EnqueueMigrateMemberEmailTask("webcie@svsticky.nl", "old@svsticky.nl", "new@svsticky.nl", _db);
        _outboxWorker.DidNotReceive().EnqueueMigrateMemberEmailTask("bar@svsticky.nl", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<PostgresDbContext>());
    }

    [Fact]
    public void OnMailChanged_DoesNothing_WhenMemberHasDirectoryEmailOverride()
    {
        var memberId = Guid.NewGuid();
        var member = new Member
        {
            Id = memberId,
            FirstName = "Bob",
            LastName = "Jones",
            Email = "new@svsticky.nl",
            DirectoryEmailOverride = "override@svsticky.nl",
            StudentNumber = "7654321",
            PhoneNumber = "0612345678",
            Street = "Heidelberglaan",
            HouseNumber = "8",
            PostalCode = "3584CS",
            City = "Utrecht"
        };
        _db.Members.Add(member);
        _db.SaveChanges();

        _listener.OnMailChanged(memberId, "old@svsticky.nl", "new@svsticky.nl", _db);

        _outboxWorker.DidNotReceiveWithAnyArgs().EnqueueMigrateMemberEmailForActiveGroupsTask(default, default!, default!, default!);
    }
}
