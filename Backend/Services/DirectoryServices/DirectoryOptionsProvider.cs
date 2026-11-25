using Backend.Database;
using Backend.Interfaces;

namespace Backend.Services.DirectoryServices;

/// <summary>
/// Loads directory configuration options from the database settings table.
/// </summary>
public class DirectoryOptionsProvider(PostgresDbContext db) : IDirectoryOptionsProvider
{
    /// <inheritdoc />
    public DirectoryOptions GetOptions()
    {
        var settings = db.Settings
            .Where(s => s.Name.StartsWith("Directory"))
            .ToDictionary(s => s.Name, s => s.Value, StringComparer.OrdinalIgnoreCase);

        var options = new DirectoryOptions();

        if (settings.TryGetValue("DirectoryEnabled", out var enabledStr) &&
            bool.TryParse(enabledStr, out var enabled))
        {
            options.Enabled = enabled;
        }

        if (settings.TryGetValue("DirectoryDryRun", out var dryRunStr) &&
            bool.TryParse(dryRunStr, out var dryRun))
        {
            options.DryRun = dryRun;
        }

        if (settings.TryGetValue("DirectoryProvider", out var provider) &&
            !string.IsNullOrWhiteSpace(provider))
        {
            options.Provider = provider;
        }

        if (settings.TryGetValue("DirectoryGoogleWorkspaceDomain", out var domain) &&
            !string.IsNullOrWhiteSpace(domain))
        {
            options.GoogleWorkspace.Domain = domain;
        }

        if (settings.TryGetValue("DirectoryGoogleWorkspaceImpersonatedAdminEmail", out var adminEmail) &&
            !string.IsNullOrWhiteSpace(adminEmail))
        {
            options.GoogleWorkspace.ImpersonatedAdminEmail = adminEmail;
        }

        if (settings.TryGetValue("DirectoryGoogleWorkspaceKeyJson", out var keyJson) &&
            !string.IsNullOrWhiteSpace(keyJson))
        {
            options.GoogleWorkspace.ServiceAccountKeyJson = keyJson;
        }

        if (settings.TryGetValue("DirectoryGoogleWorkspaceKeyPath", out var keyPath) &&
            !string.IsNullOrWhiteSpace(keyPath))
        {
            options.GoogleWorkspace.ServiceAccountKeyPath = keyPath;
        }

        return options;
    }
}
