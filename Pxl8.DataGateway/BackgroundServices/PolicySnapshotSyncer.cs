using System.Text.Json;
using Microsoft.Extensions.Options;
using Pxl8.ControlApi.Contracts.V1.PolicySnapshot;
using Pxl8.DataGateway.Configuration;
using Pxl8.DataGateway.Services;

namespace Pxl8.DataGateway.BackgroundServices;

/// <summary>
/// Background worker: Pull policy snapshots from Control Plane every 60s
/// </summary>
public class PolicySnapshotSyncer : BackgroundService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPolicySnapshotCache _policyCache;
    private readonly DataPlaneOptions _options;
    private readonly ILogger<PolicySnapshotSyncer> _logger;

    public PolicySnapshotSyncer(
        IHttpClientFactory httpClientFactory,
        IPolicySnapshotCache policyCache,
        IOptions<DataPlaneOptions> options,
        ILogger<PolicySnapshotSyncer> logger)
    {
        _httpClientFactory = httpClientFactory;
        _policyCache = policyCache;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "PolicySnapshotSyncer started. Sync interval: {Interval}, Control API: {ControlApiUrl}",
            _options.PolicySyncInterval, _options.ControlApiUrl);

        // Initial sync immediately
        await SyncPolicySnapshotAsync(stoppingToken);

        // Periodic sync
        using var timer = new PeriodicTimer(_options.PolicySyncInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
                await SyncPolicySnapshotAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
                _logger.LogInformation("PolicySnapshotSyncer stopped");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in PolicySnapshotSyncer main loop");
            }
        }
    }

    private async Task SyncPolicySnapshotAsync(CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ControlApi");
            var url = $"{_options.ControlApiUrl}/internal/policy-snapshot";

            _logger.LogDebug("Fetching policy snapshot from {Url}", url);

            var response = await client.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Failed to fetch policy snapshot: {StatusCode} {ReasonPhrase}",
                    response.StatusCode, response.ReasonPhrase);
                return;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var snapshot = JsonSerializer.Deserialize<PolicySnapshotDto>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (snapshot == null)
            {
                _logger.LogWarning("Failed to deserialize policy snapshot (null result)");
                return;
            }

            // Update cache
            _policyCache.UpdateSnapshot(snapshot);

            _logger.LogInformation(
                "Policy snapshot synced successfully: snapshot_id={SnapshotId}, tenants={TenantCount}",
                snapshot.SnapshotId, snapshot.Tenants.Count);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error fetching policy snapshot from Control API");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON deserialization error for policy snapshot");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error syncing policy snapshot");
        }
    }
}
