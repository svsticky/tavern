using Backend.Database;
using Backend.Models.Domain;
using Backend.Services.DirectoryServices;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Backend.Tests.Services;

public class DirectoryOptionsProviderTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<PostgresDbContext> _dbOptions;
    private readonly PostgresDbContext _db;

    public DirectoryOptionsProviderTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        _dbOptions = new DbContextOptionsBuilder<PostgresDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new PostgresDbContext(_dbOptions);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public void GetOptions_ReturnsDefaults_WhenNoSettingsExist()
    {
        var provider = new DirectoryOptionsProvider(_db);
        var options = provider.GetOptions();

        Assert.False(options.Enabled);
        Assert.False(options.DryRun);
        Assert.Equal("GoogleWorkspace", options.Provider);
        Assert.Equal("svsticky.nl", options.GoogleWorkspace.Domain);
        Assert.Equal("admin-sync@svsticky.nl", options.GoogleWorkspace.ImpersonatedAdminEmail);
        Assert.Null(options.GoogleWorkspace.ServiceAccountKeyJson);
        Assert.Null(options.GoogleWorkspace.ServiceAccountKeyPath);
    }

    [Fact]
    public void GetOptions_LoadsValuesFromSettingsTable()
    {
        _db.Settings.AddRange(
            new Setting { Name = "DirectoryEnabled", Value = "true" },
            new Setting { Name = "DirectoryDryRun", Value = "true" },
            new Setting { Name = "DirectoryProvider", Value = "GoogleWorkspace" },
            new Setting { Name = "DirectoryGoogleWorkspaceDomain", Value = "example.com" },
            new Setting { Name = "DirectoryGoogleWorkspaceImpersonatedAdminEmail", Value = "admin@example.com" },
            new Setting { Name = "DirectoryGoogleWorkspaceKeyJson", Value = "{\"key\": \"secret\"}" },
            new Setting { Name = "DirectoryGoogleWorkspaceKeyPath", Value = "/path/to/key.json" }
        );
        _db.SaveChanges();

        var provider = new DirectoryOptionsProvider(_db);
        var options = provider.GetOptions();

        Assert.True(options.Enabled);
        Assert.True(options.DryRun);
        Assert.Equal("GoogleWorkspace", options.Provider);
        Assert.Equal("example.com", options.GoogleWorkspace.Domain);
        Assert.Equal("admin@example.com", options.GoogleWorkspace.ImpersonatedAdminEmail);
        Assert.Equal("{\"key\": \"secret\"}", options.GoogleWorkspace.ServiceAccountKeyJson);
        Assert.Equal("/path/to/key.json", options.GoogleWorkspace.ServiceAccountKeyPath);
    }
}
