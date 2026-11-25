using Backend.Database;
using Backend.Interfaces;
using Backend.Models.Domain;
using Backend.Services.DirectoryServices;
using Backend.Utils.DateTime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Backend.Services.OutboxWorkers;

/// <summary>
/// Background worker that processes queued external directory synchronization tasks.
/// </summary>
public class DirectoryOutboxWorker(
    IServiceProvider serviceProvider,
    ILogger<DirectoryOutboxWorker> logger) : BackgroundService
{
    /// <summary>
    /// Enqueues a task to add a member to an external directory group.
    /// </summary>
    public virtual void EnqueueAddMemberTask(string groupEmail, string memberEmail, PostgresDbContext db)
    {
        var task = new DirectoryOutboxTask
        {
            TaskType = DirectoryOutboxTaskType.AddMember,
            GroupEmail = groupEmail,
            MemberEmail = memberEmail,
            CreatedAt = DateTimeOffset.UtcNow,
            NextAttemptAt = DateTimeOffset.UtcNow,
            RetryCount = 0
        };

        db.DirectoryOutboxTasks.Add(task);
        db.SaveChanges();
        logger.LogInformation("Enqueued directory AddMember task for {MemberEmail} in {GroupEmail}.", memberEmail, groupEmail);
    }

    /// <summary>
    /// Enqueues a task to remove a member from an external directory group.
    /// </summary>
    public virtual void EnqueueRemoveMemberTask(string groupEmail, string memberEmail, PostgresDbContext db)
    {
        var task = new DirectoryOutboxTask
        {
            TaskType = DirectoryOutboxTaskType.RemoveMember,
            GroupEmail = groupEmail,
            MemberEmail = memberEmail,
            CreatedAt = DateTimeOffset.UtcNow,
            NextAttemptAt = DateTimeOffset.UtcNow,
            RetryCount = 0
        };

        db.DirectoryOutboxTasks.Add(task);
        db.SaveChanges();
        logger.LogInformation("Enqueued directory RemoveMember task for {MemberEmail} in {GroupEmail}.", memberEmail, groupEmail);
    }

    /// <summary>
    /// Enqueues a task to migrate a member's email in an external directory group.
    /// </summary>
    public virtual void EnqueueMigrateMemberEmailTask(string groupEmail, string oldEmail, string newEmail, PostgresDbContext db)
    {
        var task = new DirectoryOutboxTask
        {
            TaskType = DirectoryOutboxTaskType.MigrateEmail,
            GroupEmail = groupEmail,
            MemberEmail = newEmail,
            OldMemberEmail = oldEmail,
            CreatedAt = DateTimeOffset.UtcNow,
            NextAttemptAt = DateTimeOffset.UtcNow,
            RetryCount = 0
        };

        db.DirectoryOutboxTasks.Add(task);
        db.SaveChanges();
        logger.LogInformation("Enqueued directory MigrateEmail task from {OldEmail} to {NewEmail} in {GroupEmail}.", oldEmail, newEmail, groupEmail);
    }

    /// <summary>
    /// Enqueues tasks to migrate a member's email across all active groups that have a linked directory group email.
    /// </summary>
    public virtual void EnqueueMigrateMemberEmailForActiveGroupsTask(Guid memberId, string oldEmail, string newEmail, PostgresDbContext db)
    {
        if (string.Equals(oldEmail, newEmail, StringComparison.OrdinalIgnoreCase))
            return;

        uint committeeYear = YearUtils.GetCommitteeYear();
        uint boardYear = YearUtils.GetBoardYear(db);

        var boardGroupIdSetting = db.Settings.FirstOrDefault(s => s.Name == "BoardGroupId")?.Value;
        uint.TryParse(boardGroupIdSetting, out uint boardGroupId);

        var activeGroupEmails = db.GroupMemberships
            .Where(gm => gm.MemberId == memberId && gm.Group.DirectoryGroupEmail != null)
            .Where(gm => (gm.GroupId == boardGroupId && gm.MembershipYear == boardYear) ||
                         (gm.GroupId != boardGroupId && gm.MembershipYear == committeeYear))
            .Select(gm => gm.Group.DirectoryGroupEmail!)
            .Distinct()
            .ToList();

        foreach (var groupEmail in activeGroupEmails)
        {
            EnqueueMigrateMemberEmailTask(groupEmail, oldEmail, newEmail, db);
        }
    }

    /// <summary>
    /// Enqueues a task to reconcile an external directory group against current Tavern members.
    /// </summary>
    public virtual void EnqueueReconcileGroupTask(string groupEmail, PostgresDbContext db)
    {
        var task = new DirectoryOutboxTask
        {
            TaskType = DirectoryOutboxTaskType.ReconcileGroup,
            GroupEmail = groupEmail,
            CreatedAt = DateTimeOffset.UtcNow,
            NextAttemptAt = DateTimeOffset.UtcNow,
            RetryCount = 0
        };

        db.DirectoryOutboxTasks.Add(task);
        db.SaveChanges();
        logger.LogInformation("Enqueued directory ReconcileGroup task for {GroupEmail}.", groupEmail);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Directory outbox worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            bool processed = await TryProcessNextTaskAsync(stoppingToken);

            if (!processed)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }

        logger.LogInformation("Directory outbox worker stopped.");
    }

    /// <summary>
    /// Attempts to process the next eligible directory outbox task.
    /// </summary>
    public async Task<bool> TryProcessNextTaskAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var optionsProvider = scope.ServiceProvider.GetService<IDirectoryOptionsProvider>();
        var options = optionsProvider != null
            ? optionsProvider.GetOptions()
            : scope.ServiceProvider.GetRequiredService<IOptions<DirectoryOptions>>().Value;

        if (!options.Enabled)
        {
            return false;
        }

        var db = scope.ServiceProvider.GetRequiredService<PostgresDbContext>();

        var task = await db.DirectoryOutboxTasks
            .OrderBy(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .FirstOrDefaultAsync(ct);

        if (task == null) return false;
        if (task.NextAttemptAt > DateTimeOffset.UtcNow) return false;

        logger.LogInformation("Processing directory outbox task {TaskType} for {GroupEmail}. Retry {RetryCount}.",
            task.TaskType, task.GroupEmail, task.RetryCount);

        var syncService = scope.ServiceProvider.GetRequiredService<IDirectoryGroupSyncService>();

        try
        {
            await HandleTaskAsync(db, syncService, task, ct);
            db.DirectoryOutboxTasks.Remove(task);
            logger.LogInformation("Completed directory outbox task {TaskType} for {GroupEmail}.", task.TaskType, task.GroupEmail);
        }
        catch (Exception ex)
        {
            HandleFailure(task, ex);
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    private static async Task HandleTaskAsync(PostgresDbContext db, IDirectoryGroupSyncService syncService, DirectoryOutboxTask task, CancellationToken ct)
    {
        switch (task.TaskType)
        {
            case DirectoryOutboxTaskType.AddMember:
                if (!string.IsNullOrWhiteSpace(task.MemberEmail))
                {
                    await syncService.AddMemberAsync(task.GroupEmail, task.MemberEmail, ct);
                }
                break;

            case DirectoryOutboxTaskType.RemoveMember:
                if (!string.IsNullOrWhiteSpace(task.MemberEmail))
                {
                    await syncService.RemoveMemberAsync(task.GroupEmail, task.MemberEmail, ct);
                }
                break;

            case DirectoryOutboxTaskType.MigrateEmail:
                if (!string.IsNullOrWhiteSpace(task.OldMemberEmail))
                {
                    await syncService.RemoveMemberAsync(task.GroupEmail, task.OldMemberEmail, ct);
                }
                if (!string.IsNullOrWhiteSpace(task.MemberEmail))
                {
                    await syncService.AddMemberAsync(task.GroupEmail, task.MemberEmail, ct);
                }
                break;

            case DirectoryOutboxTaskType.ReconcileGroup:
                var group = await db.Groups
                    .Include(g => g.GroupMemberships)
                    .ThenInclude(gm => gm.Member)
                    .FirstOrDefaultAsync(g => g.DirectoryGroupEmail == task.GroupEmail, ct);

                if (group != null)
                {
                    uint currentYear = YearUtils.GetCurrentYearForGroup(db, group.Id);
                    var activeEmails = group.GroupMemberships
                        .Where(gm => gm.MembershipYear == currentYear && !gm.Member.IsDeleted)
                        .Select(gm => gm.Member.GetDirectoryEmail())
                        .Where(email => !string.IsNullOrWhiteSpace(email))
                        .Distinct()
                        .ToList();

                    await syncService.ReconcileGroupAsync(task.GroupEmail, activeEmails, ct);
                }
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(task.TaskType), $"Unknown directory outbox task type {task.TaskType}.");
        }
    }

    private void HandleFailure(DirectoryOutboxTask task, Exception ex)
    {
        logger.LogError(ex, "Directory sync failed for {GroupEmail} (Task: {TaskType}). Retry count: {Count}",
            task.GroupEmail, task.TaskType, task.RetryCount);

        task.RetryCount++;

        // Exponential backoff with a max delay of 1 hour
        double extraSeconds = Math.Min(Math.Pow(2, task.RetryCount), 3600);
        task.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(extraSeconds);
        logger.LogWarning("Rescheduled directory task {TaskType} for {GroupEmail} at {NextRunUtc}.",
            task.TaskType, task.GroupEmail, task.NextAttemptAt);
    }
}
