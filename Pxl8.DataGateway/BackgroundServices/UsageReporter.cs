using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Pxl8.DataGateway.Contracts.V1.UsageReporting;
using Pxl8.DataGateway.Configuration;
using Pxl8.DataGateway.Services;

namespace Pxl8.DataGateway.BackgroundServices;

/// <summary>
/// Background worker: Push usage reports to Control Plane every 10-30s
/// </summary>
public class UsageReporter : BackgroundService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IBudgetManager _budgetManager;
    private readonly DataPlaneOptions _options;
    private readonly ILogger<UsageReporter> _logger;

    public UsageReporter(
        IHttpClientFactory httpClientFactory,
        IBudgetManager budgetManager,
        IOptions<DataPlaneOptions> options,
        ILogger<UsageReporter> logger)
    {
        _httpClientFactory = httpClientFactory;
        _budgetManager = budgetManager;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "UsageReporter started. Flush interval: {Interval}, Control API: {ControlApiUrl}",
            _options.UsageFlushInterval, _options.ControlApiUrl);

        using var timer = new PeriodicTimer(_options.UsageFlushInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
                await FlushUsageReportsAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Final flush before shutdown
                _logger.LogInformation("UsageReporter stopping, performing final flush...");
                await FlushUsageReportsAsync(CancellationToken.None);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in UsageReporter main loop");
            }
        }
    }

    private async Task FlushUsageReportsAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Get all tenants with pending usage
            var tenantsWithUsage = _budgetManager.GetAllTenantsWithPendingUsage().ToList();

            if (tenantsWithUsage.Count == 0)
            {
                _logger.LogTrace("No pending usage to report");
                return;
            }

            _logger.LogDebug("Flushing usage reports for {Count} tenant/period pairs", tenantsWithUsage.Count);

            var client = _httpClientFactory.CreateClient("ControlApi");
            var url = $"{_options.ControlApiUrl}/internal/v1/usage/report";

            foreach (var (tenantId, periodId) in tenantsWithUsage)
            {
                // Get and reset deltas
                var (bandwidthDelta, transformsDelta) = _budgetManager.GetAndResetConsumedDelta(tenantId, periodId);

                // Skip if no actual usage (edge case: delta was reset between GetAllTenantsWithPendingUsage and here)
                if (bandwidthDelta == 0 && transformsDelta == 0)
                {
                    continue;
                }

                // Create usage report
                var report = new UsageReportRequest
                {
                    ReportId = Guid.NewGuid(), // Unique per report for idempotency
                    DataplaneId = _options.DataPlaneId,
                    TenantId = tenantId,
                    PeriodId = periodId,
                    BandwidthUsedBytes = bandwidthDelta,
                    TransformsUsed = transformsDelta,
                    ReportedAt = DateTimeOffset.UtcNow
                };

                // Send to Control Plane
                try
                {
                    var response = await client.PostAsJsonAsync(url, report, cancellationToken);

                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation(
                            "Usage reported for tenant {TenantId}: bandwidth={Bandwidth}bytes, transforms={Transforms}, report_id={ReportId}",
                            tenantId, bandwidthDelta, transformsDelta, report.ReportId);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Failed to report usage for tenant {TenantId}: {StatusCode} {ReasonPhrase}",
                            tenantId, response.StatusCode, response.ReasonPhrase);

                        // TODO: Implement retry logic or dead-letter queue
                        // For now, deltas are lost (acceptable for MVP - Control Plane will see lower usage)
                    }
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogError(ex,
                        "HTTP error reporting usage for tenant {TenantId} (bandwidth={Bandwidth}, transforms={Transforms})",
                        tenantId, bandwidthDelta, transformsDelta);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error flushing usage reports");
        }
    }
}
