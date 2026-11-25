namespace Backend.Models.Domain;

/// <summary>
/// Identifies which operation a directory outbox task should perform.
/// </summary>
public enum DirectoryOutboxTaskType
{
    /// <summary>
    /// Add a member to the external directory group.
    /// </summary>
    AddMember,

    /// <summary>
    /// Remove a member from the external directory group.
    /// </summary>
    RemoveMember,

    /// <summary>
    /// Migrate a member's email address within the external directory group.
    /// </summary>
    MigrateEmail,

    /// <summary>
    /// Fully reconcile the directory group against active Tavern members for the current association year.
    /// </summary>
    ReconcileGroup
}

/// <summary>
/// Represents a task in the outbox for external directory group operations.
/// </summary>
public class DirectoryOutboxTask
{
    /// <summary>
    /// The unique identifier for the outbox task.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// The type of directory operation this task should perform.
    /// </summary>
    public required DirectoryOutboxTaskType TaskType { get; set; }

    /// <summary>
    /// The target external directory group email address.
    /// </summary>
    public required string GroupEmail { get; set; }

    /// <summary>
    /// The member's email address. Set for AddMember, RemoveMember, and MigrateEmail tasks.
    /// </summary>
    public string? MemberEmail { get; set; }

    /// <summary>
    /// The member's previous email address. Set for MigrateEmail tasks.
    /// </summary>
    public string? OldMemberEmail { get; set; }

    /// <summary>
    /// The timestamp indicating when the outbox task was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// The timestamp indicating when this task is eligible for its next processing attempt.
    /// </summary>
    public DateTimeOffset NextAttemptAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// The number of times this outbox task has been retried.
    /// </summary>
    public int RetryCount { get; set; } = 0;
}
