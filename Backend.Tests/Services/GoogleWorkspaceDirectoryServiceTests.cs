using Backend.Database;
using Backend.Interfaces;
using Backend.Services.DirectoryServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Backend.Tests.Services;

public class GoogleWorkspaceDirectoryServiceTests
{
    [Fact]
    public void ProviderName_IsGoogleWorkspace()
    {
        var options = Options.Create(new DirectoryOptions { Enabled = false });
        var service = new GoogleWorkspaceDirectoryService(options, NullLogger<GoogleWorkspaceDirectoryService>.Instance);

        Assert.Equal("GoogleWorkspace", service.ProviderName);
    }

    [Fact]
    public async Task EnsureGroupExistsAsync_ReturnsFalse_WhenDisabled()
    {
        var options = Options.Create(new DirectoryOptions { Enabled = false });
        var service = new GoogleWorkspaceDirectoryService(options, NullLogger<GoogleWorkspaceDirectoryService>.Instance);

        var result = await service.EnsureGroupExistsAsync("test@svsticky.nl", "Test Group");

        Assert.False(result);
    }

    [Fact]
    public async Task AddMemberAsync_ReturnsEarly_WhenDisabled()
    {
        var options = Options.Create(new DirectoryOptions { Enabled = false });
        var service = new GoogleWorkspaceDirectoryService(options, NullLogger<GoogleWorkspaceDirectoryService>.Instance);

        // Should return without throwing
        await service.AddMemberAsync("group@svsticky.nl", "member@svsticky.nl");
    }

    [Fact]
    public async Task RemoveMemberAsync_ReturnsEarly_WhenDisabled()
    {
        var options = Options.Create(new DirectoryOptions { Enabled = false });
        var service = new GoogleWorkspaceDirectoryService(options, NullLogger<GoogleWorkspaceDirectoryService>.Instance);

        // Should return without throwing
        await service.RemoveMemberAsync("group@svsticky.nl", "member@svsticky.nl");
    }

    [Fact]
    public async Task ReconcileGroupAsync_ReturnsZeroCounts_WhenDisabled()
    {
        var options = Options.Create(new DirectoryOptions { Enabled = false, DryRun = true });
        var service = new GoogleWorkspaceDirectoryService(options, NullLogger<GoogleWorkspaceDirectoryService>.Instance);

        var result = await service.ReconcileGroupAsync("group@svsticky.nl", ["member@svsticky.nl"]);

        Assert.Equal("group@svsticky.nl", result.GroupEmail);
        Assert.Equal(0, result.AddedCount);
        Assert.Equal(0, result.RemovedCount);
        Assert.Equal(0, result.UnchangedCount);
        Assert.True(result.DryRun);
    }

    [Fact]
    public void DependencyInjection_CanResolveService_WithValidateOnBuild()
    {
        var services = new ServiceCollection();
        var dbOptions = new DbContextOptionsBuilder<PostgresDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        services.AddScoped(_ => new PostgresDbContext(dbOptions));
        services.AddLogging();
        services.Configure<DirectoryOptions>(_ => { });
        services.AddScoped<IDirectoryOptionsProvider, DirectoryOptionsProvider>();
        services.AddScoped<DirectoryOptions>(sp => sp.GetRequiredService<IDirectoryOptionsProvider>().GetOptions());
        services.AddScoped(sp => new GoogleWorkspaceDirectoryService(
            sp.GetRequiredService<IDirectoryOptionsProvider>(),
            sp.GetRequiredService<ILogger<GoogleWorkspaceDirectoryService>>()));
        services.AddScoped<IDirectoryGroupSyncService>(sp => sp.GetRequiredService<GoogleWorkspaceDirectoryService>());

        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        using var scope = provider.CreateScope();
        var resolvedService = scope.ServiceProvider.GetRequiredService<GoogleWorkspaceDirectoryService>();
        var syncService = scope.ServiceProvider.GetRequiredService<IDirectoryGroupSyncService>();
        var activatorService = ActivatorUtilities.CreateInstance<GoogleWorkspaceDirectoryService>(scope.ServiceProvider);

        Assert.NotNull(resolvedService);
        Assert.NotNull(syncService);
        Assert.NotNull(activatorService);
    }
}
