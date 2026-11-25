using Backend.Database;
using Backend.Interfaces;
using Backend.Services.OutboxWorkers;
using Backend.Utils.DateTime;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services.DirectoryServices;

/// <summary>
/// Listens for member email changes and updates external directory groups accordingly.
/// </summary>
public class DirectoryMailChangedListener(
    DirectoryOutboxWorker directoryOutboxWorker) : IMailChangedListener
{
    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public void OnMailChanged(Guid memberId, string oldEmail, string newEmail, PostgresDbContext db)
    {
        if (string.Equals(oldEmail, newEmail, StringComparison.OrdinalIgnoreCase))
            return;

        var member = db.Members.Find(memberId);
        // If the member has an explicit directory email override, their directory email is unaffected by Koala email changes.
        if (member != null && !string.IsNullOrWhiteSpace(member.DirectoryEmailOverride))
            return;

        directoryOutboxWorker.EnqueueMigrateMemberEmailForActiveGroupsTask(memberId, oldEmail, newEmail, db);
    }
}
