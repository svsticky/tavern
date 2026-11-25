using Backend.Database;
using Backend.Interfaces;
using Backend.Models.Domain;
using Backend.Services.DirectoryServices;
using Backend.Services.OutboxWorkers;
using Backend.Utils.DateTime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Backend.Tests.Services;

public class DirectoryOutboxWorkerTests
{
    private readonly DbContextOptions<PostgresDbContext> _dbOptions;
    private readonly IDirectoryGroupSyncService _syncService;
    private readonly ILogger<DirectoryOutboxWorker> _logger;

    public DirectoryOutboxWorkerTests()
    {
        _dbOptions = new DbContextOptionsBuilder<PostgresDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _syncService = Substitute.For<IDirectoryGroupSyncService>();
        _logger = NullLogger<DirectoryOutboxWorker>.Instance;
    }

    private ServiceProvider CreateServiceProvider(PostgresDbContext db, bool enabled = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton(_syncService);
        services.AddSingleton<IOptions<DirectoryOptions>>(Options.Create(new DirectoryOptions { Enabled = enabled }));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task EnqueueAddMemberTask_SavesTaskToDatabase()
    {
        using var db = new PostgresDbContext(_dbOptions);
        var provider = CreateServiceProvider(db);
        var worker = new DirectoryOutboxWorker(provider, _logger);

        worker.EnqueueAddMemberTask("test-commissie@svsticky.nl", "member@svsticky.nl", db);

        var tasks = await db.DirectoryOutboxTasks.ToListAsync();
        Assert.Single(tasks);
        Assert.Equal(DirectoryOutboxTaskType.AddMember, tasks[0].TaskType);
        Assert.Equal("test-commissie@svsticky.nl", tasks[0].GroupEmail);
        Assert.Equal("member@svsticky.nl", tasks[0].MemberEmail);
        Assert.Equal(0, tasks[0].RetryCount);
    }

    [Fact]
    public async Task EnqueueRemoveMemberTask_SavesTaskToDatabase()
    {
        using var db = new PostgresDbContext(_dbOptions);
        var provider = CreateServiceProvider(db);
        var worker = new DirectoryOutboxWorker(provider, _logger);

        worker.EnqueueRemoveMemberTask("test-commissie@svsticky.nl", "member@svsticky.nl", db);

        var tasks = await db.DirectoryOutboxTasks.ToListAsync();
        Assert.Single(tasks);
        Assert.Equal(DirectoryOutboxTaskType.RemoveMember, tasks[0].TaskType);
        Assert.Equal("test-commissie@svsticky.nl", tasks[0].GroupEmail);
        Assert.Equal("member@svsticky.nl", tasks[0].MemberEmail);
    }

    [Fact]
    public async Task EnqueueMigrateMemberEmailTask_SavesTaskToDatabase()
    {
        using var db = new PostgresDbContext(_dbOptions);
        var provider = CreateServiceProvider(db);
        var worker = new DirectoryOutboxWorker(provider, _logger);

        worker.EnqueueMigrateMemberEmailTask("test-commissie@svsticky.nl", "old@svsticky.nl", "new@svsticky.nl", db);

        var tasks = await db.DirectoryOutboxTasks.ToListAsync();
        Assert.Single(tasks);
        Assert.Equal(DirectoryOutboxTaskType.MigrateEmail, tasks[0].TaskType);
        Assert.Equal("test-commissie@svsticky.nl", tasks[0].GroupEmail);
        Assert.Equal("old@svsticky.nl", tasks[0].OldMemberEmail);
        Assert.Equal("new@svsticky.nl", tasks[0].MemberEmail);
    }

    [Fact]
    public async Task EnqueueReconcileGroupTask_SavesTaskToDatabase()
    {
        using var db = new PostgresDbContext(_dbOptions);
        var provider = CreateServiceProvider(db);
        var worker = new DirectoryOutboxWorker(provider, _logger);

        worker.EnqueueReconcileGroupTask("test-commissie@svsticky.nl", db);

        var tasks = await db.DirectoryOutboxTasks.ToListAsync();
        Assert.Single(tasks);
        Assert.Equal(DirectoryOutboxTaskType.ReconcileGroup, tasks[0].TaskType);
        Assert.Equal("test-commissie@svsticky.nl", tasks[0].GroupEmail);
    }

    [Fact]
    public async Task TryProcessNextTaskAsync_ReturnsFalse_WhenNoTasks()
    {
        using var db = new PostgresDbContext(_dbOptions);
        var provider = CreateServiceProvider(db);
        var worker = new DirectoryOutboxWorker(provider, _logger);

        var processed = await worker.TryProcessNextTaskAsync(CancellationToken.None);

        Assert.False(processed);
    }

    [Fact]
    public async Task TryProcessNextTaskAsync_ReturnsFalse_WhenNextAttemptInFuture()
    {
        using var db = new PostgresDbContext(_dbOptions);
        db.DirectoryOutboxTasks.Add(new DirectoryOutboxTask
        {
            TaskType = DirectoryOutboxTaskType.AddMember,
            GroupEmail = "group@svsticky.nl",
            MemberEmail = "user@svsticky.nl",
            NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(10)
        });
        await db.SaveChangesAsync();

        var provider = CreateServiceProvider(db);
        var worker = new DirectoryOutboxWorker(provider, _logger);

        var processed = await worker.TryProcessNextTaskAsync(CancellationToken.None);

        Assert.False(processed);
        Assert.Single(await db.DirectoryOutboxTasks.ToListAsync());
    }

    [Fact]
    public async Task TryProcessNextTaskAsync_ProcessesAddMemberSuccessfully()
    {
        using var db = new PostgresDbContext(_dbOptions);
        db.DirectoryOutboxTasks.Add(new DirectoryOutboxTask
        {
            TaskType = DirectoryOutboxTaskType.AddMember,
            GroupEmail = "group@svsticky.nl",
            MemberEmail = "user@svsticky.nl"
        });
        await db.SaveChangesAsync();

        var provider = CreateServiceProvider(db);
        var worker = new DirectoryOutboxWorker(provider, _logger);

        var processed = await worker.TryProcessNextTaskAsync(CancellationToken.None);

        Assert.True(processed);
        await _syncService.Received(1).AddMemberAsync("group@svsticky.nl", "user@svsticky.nl", Arg.Any<CancellationToken>());
        Assert.Empty(await db.DirectoryOutboxTasks.ToListAsync());
    }

    [Fact]
    public async Task TryProcessNextTaskAsync_ProcessesRemoveMemberSuccessfully()
    {
        using var db = new PostgresDbContext(_dbOptions);
        db.DirectoryOutboxTasks.Add(new DirectoryOutboxTask
        {
            TaskType = DirectoryOutboxTaskType.RemoveMember,
            GroupEmail = "group@svsticky.nl",
            MemberEmail = "user@svsticky.nl"
        });
        await db.SaveChangesAsync();

        var provider = CreateServiceProvider(db);
        var worker = new DirectoryOutboxWorker(provider, _logger);

        var processed = await worker.TryProcessNextTaskAsync(CancellationToken.None);

        Assert.True(processed);
        await _syncService.Received(1).RemoveMemberAsync("group@svsticky.nl", "user@svsticky.nl", Arg.Any<CancellationToken>());
        Assert.Empty(await db.DirectoryOutboxTasks.ToListAsync());
    }

    [Fact]
    public async Task TryProcessNextTaskAsync_ProcessesMigrateEmailSuccessfully()
    {
        using var db = new PostgresDbContext(_dbOptions);
        db.DirectoryOutboxTasks.Add(new DirectoryOutboxTask
        {
            TaskType = DirectoryOutboxTaskType.MigrateEmail,
            GroupEmail = "group@svsticky.nl",
            OldMemberEmail = "old@svsticky.nl",
            MemberEmail = "new@svsticky.nl"
        });
        await db.SaveChangesAsync();

        var provider = CreateServiceProvider(db);
        var worker = new DirectoryOutboxWorker(provider, _logger);

        var processed = await worker.TryProcessNextTaskAsync(CancellationToken.None);

        Assert.True(processed);
        await _syncService.Received(1).RemoveMemberAsync("group@svsticky.nl", "old@svsticky.nl", Arg.Any<CancellationToken>());
        await _syncService.Received(1).AddMemberAsync("group@svsticky.nl", "new@svsticky.nl", Arg.Any<CancellationToken>());
        Assert.Empty(await db.DirectoryOutboxTasks.ToListAsync());
    }

    [Fact]
    public async Task TryProcessNextTaskAsync_ReconcileGroup_UsesDirectoryEmailOverride_WhenSet()
    {
        using var db = new PostgresDbContext(_dbOptions);
        uint groupId = 600;
        uint currentCommitteeYear = YearUtils.GetCommitteeYear();

        var memberWithOverride = new Member
        {
            Id = Guid.NewGuid(),
            FirstName = "Override",
            LastName = "User",
            Email = "koala@example.com",
            DirectoryEmailOverride = "override@svsticky.nl",
            StudentNumber = "s6001",
            PhoneNumber = "0612345678",
            Street = "Street",
            HouseNumber = "1",
            PostalCode = "1234AB",
            City = "City"
        };
        db.Members.Add(memberWithOverride);

        var group = new Group
        {
            Id = groupId,
            Name = "Active Committee 2",
            Type = GroupType.Committee,
            DirectoryGroupEmail = "active2-commissie@svsticky.nl",
            GroupMemberships =
            [
                new GroupMembership
                {
                    GroupId = groupId,
                    MemberId = memberWithOverride.Id,
                    Member = memberWithOverride,
                    MembershipYear = currentCommitteeYear
                }
            ]
        };
        db.Groups.Add(group);

        db.DirectoryOutboxTasks.Add(new DirectoryOutboxTask
        {
            TaskType = DirectoryOutboxTaskType.ReconcileGroup,
            GroupEmail = "active2-commissie@svsticky.nl"
        });
        await db.SaveChangesAsync();

        var provider = CreateServiceProvider(db);
        var worker = new DirectoryOutboxWorker(provider, _logger);

        var processed = await worker.TryProcessNextTaskAsync(CancellationToken.None);

        Assert.True(processed);
        await _syncService.Received(1).ReconcileGroupAsync(
            "active2-commissie@svsticky.nl",
            Arg.Is<IReadOnlyCollection<string>>(emails => emails.Count == 1 && emails.Contains("override@svsticky.nl") && !emails.Contains("koala@example.com")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryProcessNextTaskAsync_IncrementsRetryAndBackoff_OnFailure()
    {
        using var db = new PostgresDbContext(_dbOptions);
        db.DirectoryOutboxTasks.Add(new DirectoryOutboxTask
        {
            TaskType = DirectoryOutboxTaskType.AddMember,
            GroupEmail = "group@svsticky.nl",
            MemberEmail = "user@svsticky.nl",
            RetryCount = 0
        });
        await db.SaveChangesAsync();

        _syncService.When(s => s.AddMemberAsync("group@svsticky.nl", "user@svsticky.nl", Arg.Any<CancellationToken>()))
            .Do(_ => throw new HttpRequestException("Network failure"));

        var provider = CreateServiceProvider(db);
        var worker = new DirectoryOutboxWorker(provider, _logger);

        var processed = await worker.TryProcessNextTaskAsync(CancellationToken.None);

        Assert.True(processed);
        var task = await db.DirectoryOutboxTasks.SingleAsync();
        Assert.Equal(1, task.RetryCount);
        Assert.True(task.NextAttemptAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task TryProcessNextTaskAsync_ProcessesReconcileGroupSuccessfully()
    {
        using var db = new PostgresDbContext(_dbOptions);
        uint groupId = 10;
        var member1 = new Member
        {
            Id = Guid.NewGuid(),
            Email = "member1@svsticky.nl",
            StudentNumber = "s1234567",
            FirstName = "First",
            LastName = "One",
            PhoneNumber = "0612345678",
            Street = "Street",
            HouseNumber = "1",
            PostalCode = "1234AB",
            City = "Utrecht"
        };
        var member2 = new Member
        {
            Id = Guid.NewGuid(),
            Email = "member2@svsticky.nl",
            StudentNumber = "s1234568",
            FirstName = "Second",
            LastName = "Two",
            PhoneNumber = "0612345679",
            Street = "Street",
            HouseNumber = "2",
            PostalCode = "1234AB",
            City = "Utrecht"
        };
        db.Members.AddRange(member1, member2);

        var currentCommitteeYear = Backend.Utils.DateTime.YearUtils.GetCommitteeYear();
        var group = new Group
        {
            Id = groupId,
            Name = "Active Committee",
            Type = GroupType.Committee,
            DirectoryGroupEmail = "active-commissie@svsticky.nl",
            GroupMemberships =
            [
                new GroupMembership
                {
                    GroupId = groupId,
                    MemberId = member1.Id,
                    Member = member1,
                    MembershipYear = currentCommitteeYear
                },
                new GroupMembership
                {
                    GroupId = groupId,
                    MemberId = member2.Id,
                    Member = member2,
                    MembershipYear = currentCommitteeYear - 1 // old year
                }
            ]
        };
        db.Groups.Add(group);

        db.DirectoryOutboxTasks.Add(new DirectoryOutboxTask
        {
            TaskType = DirectoryOutboxTaskType.ReconcileGroup,
            GroupEmail = "active-commissie@svsticky.nl"
        });
        await db.SaveChangesAsync();

        var provider = CreateServiceProvider(db);
        var worker = new DirectoryOutboxWorker(provider, _logger);

        var processed = await worker.TryProcessNextTaskAsync(CancellationToken.None);

        Assert.True(processed);
        await _syncService.Received(1).ReconcileGroupAsync(
            "active-commissie@svsticky.nl",
            Arg.Is<IReadOnlyCollection<string>>(emails => emails.Count == 1 && emails.Contains("member1@svsticky.nl")),
            Arg.Any<CancellationToken>());
        Assert.Empty(await db.DirectoryOutboxTasks.ToListAsync());
    }

    [Fact]
    public async Task TryProcessNextTaskAsync_DisabledDirectory_NoOps()
    {
        using var db = new PostgresDbContext(_dbOptions);
        db.DirectoryOutboxTasks.Add(new DirectoryOutboxTask
        {
            TaskType = DirectoryOutboxTaskType.AddMember,
            GroupEmail = "group@svsticky.nl",
            MemberEmail = "user@svsticky.nl"
        });
        await db.SaveChangesAsync();

        var provider = CreateServiceProvider(db, enabled: false);
        var worker = new DirectoryOutboxWorker(provider, _logger);

        var processed = await worker.TryProcessNextTaskAsync(CancellationToken.None);

        Assert.False(processed);
        Assert.Single(await db.DirectoryOutboxTasks.ToListAsync());
        await _syncService.DidNotReceiveWithAnyArgs().AddMemberAsync(default!, default!, default);
    }

    [Fact]
    public async Task ExecuteAsync_ProcessesTasksAndDelays()
    {
        using var db = new PostgresDbContext(_dbOptions);
        var provider = CreateServiceProvider(db);
        var worker = new DirectoryOutboxWorker(provider, _logger);

        using var cts = new CancellationTokenSource();
        var startTask = worker.StartAsync(cts.Token);

        await Task.Delay(100);

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
        await startTask;
    }
}
