using Backend.Services.DirectoryServices;

namespace Backend.Interfaces;

/// <summary>
/// Provides configuration options for external directory synchronization.
/// </summary>
public interface IDirectoryOptionsProvider
{
    /// <summary>
    /// Gets the current directory options loaded from application settings.
    /// </summary>
    /// <returns>The directory configuration options.</returns>
    DirectoryOptions GetOptions();
}
