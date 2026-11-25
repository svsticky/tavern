using Backend.Interfaces;
using Google;
using Google.Apis.Admin.Directory.directory_v1;
using Google.Apis.Admin.Directory.directory_v1.Data;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using System.Net;

namespace Backend.Services.DirectoryServices;

/// <summary>
/// Google Workspace implementation of <see cref="IDirectoryGroupSyncService"/> using the Google Admin Directory API v1.
/// </summary>
public class GoogleWorkspaceDirectoryService : IDirectoryGroupSyncService
{
    private readonly DirectoryOptions _options;
    private readonly ILogger<GoogleWorkspaceDirectoryService> _logger;
    private DirectoryService? _directoryService;
    private readonly ResiliencePipeline _retryPipeline;

    /// <inheritdoc />
    public string ProviderName => "GoogleWorkspace";

    /// <summary>
    /// Initializes a new instance of <see cref="GoogleWorkspaceDirectoryService"/> using an options provider.
    /// </summary>
    /// <param name="optionsProvider">Directory options provider.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="directoryService">Optional pre-configured DirectoryService instance (primarily for testing).</param>
    [ActivatorUtilitiesConstructor]
    public GoogleWorkspaceDirectoryService(
        IDirectoryOptionsProvider optionsProvider,
        ILogger<GoogleWorkspaceDirectoryService> logger,
        DirectoryService? directoryService = null)
        : this(optionsProvider.GetOptions(), logger, directoryService)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="GoogleWorkspaceDirectoryService"/> using IOptions.
    /// </summary>
    /// <param name="options">Directory configuration options.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="directoryService">Optional pre-configured DirectoryService instance (primarily for testing).</param>
    public GoogleWorkspaceDirectoryService(
        IOptions<DirectoryOptions> options,
        ILogger<GoogleWorkspaceDirectoryService> logger,
        DirectoryService? directoryService = null)
        : this(options.Value, logger, directoryService)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="GoogleWorkspaceDirectoryService"/> directly with options.
    /// </summary>
    /// <param name="options">Directory configuration options.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="directoryService">Optional pre-configured DirectoryService instance (primarily for testing).</param>
    public GoogleWorkspaceDirectoryService(
        DirectoryOptions options,
        ILogger<GoogleWorkspaceDirectoryService> logger,
        DirectoryService? directoryService = null)
    {
        _options = options;
        _logger = logger;
        _directoryService = directoryService;

        _retryPipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<GoogleApiException>(ex =>
                    ex.HttpStatusCode == HttpStatusCode.TooManyRequests ||
                    (ex.HttpStatusCode == HttpStatusCode.Forbidden &&
                     ex.Error?.Errors?.Any(e => e.Reason == "quotaExceeded" || e.Reason == "rateLimitExceeded") == true)),
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential
            })
            .Build();
    }

    /// <inheritdoc />
    public async Task<bool> EnsureGroupExistsAsync(string email, string name, string? description = null, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Directory sync is disabled; skipping EnsureGroupExists for {Email}.", email);
            return false;
        }

        var service = GetOrCreateDirectoryService();

        try
        {
            await _retryPipeline.ExecuteAsync(async state =>
            {
                return await service.Groups.Get(email).ExecuteAsync(state);
            }, ct);

            return true;
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            if (_options.DryRun)
            {
                _logger.LogInformation("[DryRun] Would create Google Workspace group {Email} ({Name}).", email, name);
                return false;
            }

            _logger.LogInformation("Creating missing Google Workspace group {Email} ({Name}).", email, name);

            var newGroup = new Google.Apis.Admin.Directory.directory_v1.Data.Group
            {
                Email = email,
                Name = name,
                Description = description ?? $"Managed by Tavern for {name}"
            };

            await _retryPipeline.ExecuteAsync(async state =>
            {
                return await service.Groups.Insert(newGroup).ExecuteAsync(state);
            }, ct);

            return true;
        }
    }

    /// <inheritdoc />
    public async Task AddMemberAsync(string groupEmail, string memberEmail, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Directory sync is disabled; skipping AddMember for {MemberEmail} in {GroupEmail}.", memberEmail, groupEmail);
            return;
        }

        if (_options.DryRun)
        {
            _logger.LogInformation("[DryRun] Would add member {MemberEmail} to Google Workspace group {GroupEmail}.", memberEmail, groupEmail);
            return;
        }

        var service = GetOrCreateDirectoryService();

        try
        {
            var member = new Member
            {
                Email = memberEmail,
                Role = "MEMBER"
            };

            await _retryPipeline.ExecuteAsync(async state =>
            {
                return await service.Members.Insert(member, groupEmail).ExecuteAsync(state);
            }, ct);

            _logger.LogInformation("Added member {MemberEmail} to Google Workspace group {GroupEmail}.", memberEmail, groupEmail);
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.Conflict ||
                                            ex.Error?.Errors?.Any(e => e.Reason == "duplicate") == true)
        {
            _logger.LogInformation("Member {MemberEmail} is already in Google Workspace group {GroupEmail}; skipping.", memberEmail, groupEmail);
        }
    }

    /// <inheritdoc />
    public async Task RemoveMemberAsync(string groupEmail, string memberEmail, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Directory sync is disabled; skipping RemoveMember for {MemberEmail} in {GroupEmail}.", memberEmail, groupEmail);
            return;
        }

        if (_options.DryRun)
        {
            _logger.LogInformation("[DryRun] Would remove member {MemberEmail} from Google Workspace group {GroupEmail}.", memberEmail, groupEmail);
            return;
        }

        var service = GetOrCreateDirectoryService();

        try
        {
            await _retryPipeline.ExecuteAsync(async state =>
            {
                return await service.Members.Delete(groupEmail, memberEmail).ExecuteAsync(state);
            }, ct);

            _logger.LogInformation("Removed member {MemberEmail} from Google Workspace group {GroupEmail}.", memberEmail, groupEmail);
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound ||
                                            ex.Error?.Errors?.Any(e => e.Reason == "notFound") == true)
        {
            _logger.LogInformation("Member {MemberEmail} not found in Google Workspace group {GroupEmail}; skipping removal.", memberEmail, groupEmail);
        }
    }

    /// <inheritdoc />
    public async Task<GroupReconciliationResult> ReconcileGroupAsync(string groupEmail, IReadOnlyCollection<string> expectedMemberEmails, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Directory sync is disabled; skipping ReconcileGroup for {GroupEmail}.", groupEmail);
            return new GroupReconciliationResult(groupEmail, 0, 0, 0, [], [], _options.DryRun);
        }

        var service = GetOrCreateDirectoryService();

        // 1. Fetch all existing members via pagination
        var existingMembers = new List<Member>();
        string? pageToken = null;

        do
        {
            var listRequest = service.Members.List(groupEmail);
            listRequest.PageToken = pageToken;

            var response = await _retryPipeline.ExecuteAsync(async state =>
            {
                return await listRequest.ExecuteAsync(state);
            }, ct);

            if (response.MembersValue != null)
            {
                existingMembers.AddRange(response.MembersValue);
            }

            pageToken = response.NextPageToken;
        } while (!string.IsNullOrEmpty(pageToken));

        // 2. Identify owners and current members (owners are protected and never removed)
        var ownerEmails = existingMembers
            .Where(m => string.Equals(m.Role, "OWNER", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(m.Email))
            .Select(m => m.Email.Trim().ToLowerInvariant())
            .ToHashSet();

        var currentNonOwnerEmails = existingMembers
            .Where(m => !string.Equals(m.Role, "OWNER", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(m.Email))
            .Select(m => m.Email.Trim().ToLowerInvariant())
            .ToHashSet();

        var normalizedExpected = expectedMemberEmails
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim().ToLowerInvariant())
            .ToHashSet();

        // 3. Compute diffs
        var toAdd = normalizedExpected
            .Except(currentNonOwnerEmails)
            .Except(ownerEmails)
            .ToList();

        var toRemove = currentNonOwnerEmails
            .Except(normalizedExpected)
            .ToList();

        int unchangedCount = normalizedExpected.Intersect(currentNonOwnerEmails).Count() +
                             normalizedExpected.Intersect(ownerEmails).Count();

        _logger.LogInformation(
            "Reconciling Google Workspace group {GroupEmail}: {ToAddCount} to add, {ToRemoveCount} to remove, {UnchangedCount} unchanged, {OwnerCount} owners preserved (DryRun: {DryRun}).",
            groupEmail, toAdd.Count, toRemove.Count, unchangedCount, ownerEmails.Count, _options.DryRun);

        // 4. Apply changes
        foreach (var email in toAdd)
        {
            await AddMemberAsync(groupEmail, email, ct);
        }

        foreach (var email in toRemove)
        {
            await RemoveMemberAsync(groupEmail, email, ct);
        }

        return new GroupReconciliationResult(
            groupEmail,
            toAdd.Count,
            toRemove.Count,
            unchangedCount,
            toAdd,
            toRemove,
            _options.DryRun
        );
    }

    private DirectoryService GetOrCreateDirectoryService()
    {
        if (_directoryService != null) return _directoryService;

#pragma warning disable CS0618 // Type or member is obsolete
        GoogleCredential credential;

        if (!string.IsNullOrWhiteSpace(_options.GoogleWorkspace.ServiceAccountKeyPath) &&
            File.Exists(_options.GoogleWorkspace.ServiceAccountKeyPath))
        {
            using var stream = File.OpenRead(_options.GoogleWorkspace.ServiceAccountKeyPath);
            credential = GoogleCredential.FromStream(stream);
        }
        else if (!string.IsNullOrWhiteSpace(_options.GoogleWorkspace.ServiceAccountKeyJson))
        {
            credential = GoogleCredential.FromJson(_options.GoogleWorkspace.ServiceAccountKeyJson);
        }
        else
        {
            throw new InvalidOperationException(
                "Google Workspace directory service account key is not configured. Provide ServiceAccountKeyPath or ServiceAccountKeyJson.");
        }
#pragma warning restore CS0618

        credential = credential
            .CreateScoped([
                DirectoryService.ScopeConstants.AdminDirectoryGroup,
                DirectoryService.ScopeConstants.AdminDirectoryGroupMember
            ])
            .CreateWithUser(_options.GoogleWorkspace.ImpersonatedAdminEmail);

        _directoryService = new DirectoryService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Tavern"
        });

        return _directoryService;
    }
}
