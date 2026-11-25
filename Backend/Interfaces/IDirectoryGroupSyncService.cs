namespace Backend.Interfaces;

/// <summary>
/// Result summary of an external directory group reconciliation.
/// </summary>
/// <param name="GroupEmail">The email address of the directory group.</param>
/// <param name="AddedCount">Number of members added to the directory group.</param>
/// <param name="RemovedCount">Number of members removed from the directory group.</param>
/// <param name="UnchangedCount">Number of members whose membership was already in sync.</param>
/// <param name="AddedEmails">List of email addresses added.</param>
/// <param name="RemovedEmails">List of email addresses removed.</param>
/// <param name="DryRun">Whether the reconciliation was executed in dry-run mode.</param>
public record GroupReconciliationResult(
    string GroupEmail,
    int AddedCount,
    int RemovedCount,
    int UnchangedCount,
    IReadOnlyList<string> AddedEmails,
    IReadOnlyList<string> RemovedEmails,
    bool DryRun
);

/// <summary>
/// Defines the abstraction for synchronizing internal Tavern groups and memberships
/// with external directory services (e.g. Google Workspace, Microsoft Entra ID).
/// </summary>
public interface IDirectoryGroupSyncService
{
    /// <summary>
    /// Identifier of the directory service provider (e.g., "GoogleWorkspace", "EntraId").
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Ensures that the specified group exists in the external directory, creating it if missing.
    /// </summary>
    /// <param name="email">The group email address.</param>
    /// <param name="name">The human-readable name of the group.</param>
    /// <param name="description">An optional description of the group.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns><c>true</c> if the group was ensured/created; <c>false</c> if skipped or failed.</returns>
    Task<bool> EnsureGroupExistsAsync(string email, string name, string? description = null, CancellationToken ct = default);

    /// <summary>
    /// Adds a member email address to an external directory group.
    /// </summary>
    /// <param name="groupEmail">The directory group email address.</param>
    /// <param name="memberEmail">The email address of the member to add.</param>
    /// <param name="ct">The cancellation token.</param>
    Task AddMemberAsync(string groupEmail, string memberEmail, CancellationToken ct = default);

    /// <summary>
    /// Removes a member email address from an external directory group.
    /// </summary>
    /// <param name="groupEmail">The directory group email address.</param>
    /// <param name="memberEmail">The email address of the member to remove.</param>
    /// <param name="ct">The cancellation token.</param>
    Task RemoveMemberAsync(string groupEmail, string memberEmail, CancellationToken ct = default);

    /// <summary>
    /// Reconciles the external directory group membership against the expected active member emails.
    /// Administrative owners (Role == "OWNER") are preserved and never removed.
    /// </summary>
    /// <param name="groupEmail">The directory group email address.</param>
    /// <param name="expectedMemberEmails">The complete set of email addresses expected in the group.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A summary of added, removed, and unchanged member counts.</returns>
    Task<GroupReconciliationResult> ReconcileGroupAsync(string groupEmail, IReadOnlyCollection<string> expectedMemberEmails, CancellationToken ct = default);
}
