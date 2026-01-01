using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Pxl8.DataGateway.Contracts.V1.BudgetAllocation;
using Pxl8.DataGateway.Configuration;
using Pxl8.DataGateway.Services;

namespace Pxl8.DataGateway.BackgroundServices;

/// <summary>
/// Background worker: Request budget refills when low (< 20% of granted)
/// </summary>
public class BudgetRefiller : BackgroundService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IBudgetManager _budgetManager;
    private readonly IPolicySnapshotCache _policyCache;
    private readonly DataPlaneOptions _options;
    private readonly ILogger<BudgetRefiller> _logger;

    public BudgetRefiller(
        IHttpClientFactory httpClientFactory,
        IBudgetManager budgetManager,
        IPolicySnapshotCache policyCache,
        IOptions<DataPlaneOptions> options,
        ILogger<BudgetRefiller> logger)
    {
        _httpClientFactory = httpClientFactory;
        _budgetManager = budgetManager;
        _policyCache = policyCache;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "BudgetRefiller started. Check interval: {Interval}, threshold: {Threshold}%",
            _options.BudgetRefillCheckInterval, _options.BudgetRefillThreshold * 100);

        using var timer = new PeriodicTimer(_options.BudgetRefillCheckInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
                await CheckAndRefillBudgetsAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("BudgetRefiller stopped");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in BudgetRefiller main loop");
            }
        }
    }

    private async Task CheckAndRefillBudgetsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var tenantsToCheck = new HashSet<(Guid tenantId, Guid periodId)>();

            // 1. Get all tenants from policy snapshot (includes new tenants needing initial budget)
            var snapshot = _policyCache.GetCurrentSnapshot();
            if (snapshot != null)
            {
                foreach (var tenant in snapshot.Tenants)
                {
                    tenantsToCheck.Add((tenant.TenantId, tenant.CurrentPeriodId));
                }
            }

            // 2. Also include tenants with pending usage (may have different periods)
            var tenantsWithUsage = _budgetManager.GetAllTenantsWithPendingUsage();
            foreach (var pair in tenantsWithUsage)
            {
                tenantsToCheck.Add(pair);
            }

            // 3. Check each tenant and request refill if needed
            foreach (var (tenantId, periodId) in tenantsToCheck)
            {
                if (_budgetManager.ShouldRefill(tenantId, periodId))
                {
                    await RequestBudgetRefillAsync(tenantId, periodId, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error checking budgets for refill");
        }
    }

    private async Task RequestBudgetRefillAsync(Guid tenantId, Guid periodId, CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ControlApi");
            var url = $"{_options.ControlApiUrl}/internal/v1/budget/allocate";

            // Create budget allocation request
            var request = new BudgetAllocateRequest
            {
                RequestId = Guid.NewGuid(), // Unique per request for idempotency
                DataplaneId = _options.DataPlaneId,
                TenantId = tenantId,
                PeriodId = periodId,
                BandwidthRequestedBytes = _options.InitialBandwidthRequest,
                TransformsRequested = _options.InitialTransformsRequest
            };

            _logger.LogInformation(
                "Requesting budget refill for tenant {TenantId}: bandwidth={Bandwidth}bytes, transforms={Transforms}, request_id={RequestId}",
                tenantId, request.BandwidthRequestedBytes, request.TransformsRequested, request.RequestId);

            var response = await client.PostAsJsonAsync(url, request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var lease = await response.Content.ReadFromJsonAsync<BudgetAllocateResponse>(cancellationToken);

                if (lease != null)
                {
                    // Grant lease to budget manager
                    _budgetManager.GrantLease(tenantId, periodId, lease);

                    _logger.LogInformation(
                        "Budget refilled for tenant {TenantId}: lease_id={LeaseId}, bandwidth={Bandwidth}bytes, transforms={Transforms}, expires_at={ExpiresAt}",
                        tenantId, lease.LeaseId, lease.BandwidthGrantedBytes, lease.TransformsGranted, lease.ExpiresAt);
                }
                else
                {
                    _logger.LogWarning("Budget allocation response deserialization failed for tenant {TenantId}", tenantId);
                }
            }
            else
            {
                _logger.LogWarning(
                    "Budget refill request failed for tenant {TenantId}: {StatusCode} {ReasonPhrase}",
                    tenantId, response.StatusCode, response.ReasonPhrase);
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error requesting budget refill for tenant {TenantId}", tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error requesting budget refill for tenant {TenantId}", tenantId);
        }
    }
}
