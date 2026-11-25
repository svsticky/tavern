namespace Backend.Services.DirectoryServices;

/// <summary>
/// Configuration options for external directory synchronization.
/// </summary>
public class DirectoryOptions
{
    /// <summary>
    /// Configuration section key in appsettings.json.
    /// </summary>
    public const string SectionName = "Directory";

    /// <summary>
    /// Which directory provider to use. Defaults to "GoogleWorkspace".
    /// Can also be "EntraId" or other supported directory systems.
    /// </summary>
    public string Provider { get; set; } = "GoogleWorkspace";

    /// <summary>
    /// Master toggle for external directory synchronization. When false, no remote mutations occur.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// When true, operations are logged as [DryRun] without mutating external directories.
    /// </summary>
    public bool DryRun { get; set; } = false;

    /// <summary>
    /// Provider-specific settings for Google Workspace.
    /// </summary>
    public GoogleWorkspaceOptions GoogleWorkspace { get; set; } = new();
}

/// <summary>
/// Provider settings specific to Google Workspace directory operations.
/// </summary>
public class GoogleWorkspaceOptions
{
    /// <summary>
    /// Path to the service account JSON key file (e.g., /secrets/google-sa.json).
    /// </summary>
    public string? ServiceAccountKeyPath { get; set; }

    /// <summary>
    /// Raw JSON content of the service account key (useful for Docker/environment variables).
    /// </summary>
    public string? ServiceAccountKeyJson { get; set; }

    /// <summary>
    /// The Google Workspace admin account email to impersonate via domain-wide delegation.
    /// </summary>
    public string ImpersonatedAdminEmail { get; set; } = "admin-sync@stickyutrecht.nl";

    /// <summary>
    /// The primary domain for the Google Workspace account.
    /// </summary>
    public string Domain { get; set; } = "stickyutrecht.nl";
}
