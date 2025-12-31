using System.Collections.Concurrent;
using Pxl8.ControlApi.Contracts.V1.BudgetAllocation;
using Pxl8.DataGateway.Models;

namespace Pxl8.DataGateway.Services;

/// <summary>
/// In-memory budget manager (hot path, zero DB calls)
/// </summary>
public class BudgetManager : IBudgetManager
{
    private readonly ConcurrentDictionary<(Guid TenantId, Guid PeriodId), TenantBudget> _budgets = new();
    private readonly ILogger<BudgetManager> _logger;

    // Refill threshold: trigger when remaining < 20% of granted
    private const double RefillThresholdPercent = 0.2;

    // Refill cooldown: don't retry refill within 10 seconds
    private static readonly TimeSpan RefillCooldown = TimeSpan.FromSeconds(10);

    public BudgetManager(ILogger<BudgetManager> logger)
    {
        _logger = logger;
    }

    public bool TrySpendBandwidth(Guid tenantId, Guid periodId, long bytes)
    {
        var budget = GetOrCreateBudget(tenantId, periodId);

        lock (budget.Lock)
        {
            // CRITICAL: Check lease expiry (hard cutoff)
            if (DateTimeOffset.UtcNow > budget.ExpiresAt)
            {
                budget.RemainingBandwidthBytes = 0;
                budget.RemainingTransforms = 0;

                _logger.LogWarning(
                    "Budget lease expired for tenant {TenantId}, period {PeriodId}. Lease {LeaseId} expired at {ExpiresAt}",
                    tenantId, periodId, budget.LeaseId, budget.ExpiresAt);

                return false;
            }

            // Check if enough budget
            if (budget.RemainingBandwidthBytes < bytes)
            {
                _logger.LogDebug(
                    "Insufficient bandwidth for tenant {TenantId}: requested {Requested}, remaining {Remaining}",
                    tenantId, bytes, budget.RemainingBandwidthBytes);

                return false;
            }

            // Spend budget
            budget.RemainingBandwidthBytes -= bytes;
            budget.ConsumedBandwidthDelta += bytes;

            _logger.LogTrace(
                "Spent {Bytes} bytes for tenant {TenantId}. Remaining: {Remaining}/{Granted}",
                bytes, tenantId, budget.RemainingBandwidthBytes, budget.GrantedBandwidthBytes);

            return true;
        }
    }

    public bool TrySpendTransform(Guid tenantId, Guid periodId)
    {
        var budget = GetOrCreateBudget(tenantId, periodId);

        lock (budget.Lock)
        {
            // Check lease expiry
            if (DateTimeOffset.UtcNow > budget.ExpiresAt)
            {
                budget.RemainingBandwidthBytes = 0;
                budget.RemainingTransforms = 0;
                return false;
            }

            // Check if enough transforms
            if (budget.RemainingTransforms <= 0)
            {
                _logger.LogDebug(
                    "Insufficient transforms for tenant {TenantId}: remaining {Remaining}",
                    tenantId, budget.RemainingTransforms);

                return false;
            }

            // Spend transform
            budget.RemainingTransforms--;
            budget.ConsumedTransformsDelta++;

            _logger.LogTrace(
                "Spent 1 transform for tenant {TenantId}. Remaining: {Remaining}/{Granted}",
                tenantId, budget.RemainingTransforms, budget.GrantedTransforms);

            return true;
        }
    }

    public void GrantLease(Guid tenantId, Guid periodId, BudgetAllocateResponse lease)
    {
        var budget = GetOrCreateBudget(tenantId, periodId);

        lock (budget.Lock)
        {
            // Update budget with new lease
            budget.LeaseId = lease.LeaseId;
            budget.RemainingBandwidthBytes = lease.BandwidthGrantedBytes;
            budget.RemainingTransforms = lease.TransformsGranted;
            budget.GrantedBandwidthBytes = lease.BandwidthGrantedBytes;
            budget.GrantedTransforms = lease.TransformsGranted;
            budget.ExpiresAt = lease.ExpiresAt;
            budget.RefillInProgress = false; // Reset refill flag

            _logger.LogInformation(
                "Granted budget lease {LeaseId} for tenant {TenantId}: bandwidth={Bandwidth}bytes, transforms={Transforms}, expires={ExpiresAt}",
                lease.LeaseId, tenantId, lease.BandwidthGrantedBytes, lease.TransformsGranted, lease.ExpiresAt);
        }
    }

    public bool ShouldRefill(Guid tenantId, Guid periodId)
    {
        var budget = GetOrCreateBudget(tenantId, periodId);

        lock (budget.Lock)
        {
            // Don't refill if already in progress
            if (budget.RefillInProgress)
            {
                return false;
            }

            // Don't refill if cooldown not elapsed
            if (DateTimeOffset.UtcNow - budget.LastRefillAttemptAt < RefillCooldown)
            {
                return false;
            }

            // Don't refill if lease expired (will get new lease anyway)
            if (DateTimeOffset.UtcNow > budget.ExpiresAt)
            {
                return false;
            }

            // Check bandwidth threshold: remaining < 20% of granted
            var bandwidthThreshold = (long)(budget.GrantedBandwidthBytes * RefillThresholdPercent);
            var shouldRefillBandwidth = budget.RemainingBandwidthBytes < bandwidthThreshold;

            // Check transforms threshold
            var transformsThreshold = (int)(budget.GrantedTransforms * RefillThresholdPercent);
            var shouldRefillTransforms = budget.RemainingTransforms < transformsThreshold;

            if (shouldRefillBandwidth || shouldRefillTransforms)
            {
                // Mark refill in progress
                budget.RefillInProgress = true;
                budget.LastRefillAttemptAt = DateTimeOffset.UtcNow;

                _logger.LogInformation(
                    "Budget refill triggered for tenant {TenantId}: bandwidth {Remaining}/{Granted} (threshold {Threshold}), transforms {RemainingT}/{GrantedT}",
                    tenantId, budget.RemainingBandwidthBytes, budget.GrantedBandwidthBytes, bandwidthThreshold,
                    budget.RemainingTransforms, budget.GrantedTransforms);

                return true;
            }

            return false;
        }
    }

    public TenantBudget? GetBudget(Guid tenantId, Guid periodId)
    {
        return _budgets.TryGetValue((tenantId, periodId), out var budget) ? budget : null;
    }

    public (long bandwidthDelta, int transformsDelta) GetAndResetConsumedDelta(Guid tenantId, Guid periodId)
    {
        var budget = GetOrCreateBudget(tenantId, periodId);

        lock (budget.Lock)
        {
            var bandwidthDelta = budget.ConsumedBandwidthDelta;
            var transformsDelta = budget.ConsumedTransformsDelta;

            // Reset deltas after reading
            budget.ConsumedBandwidthDelta = 0;
            budget.ConsumedTransformsDelta = 0;

            return (bandwidthDelta, transformsDelta);
        }
    }

    public IEnumerable<(Guid TenantId, Guid PeriodId)> GetAllTenantsWithPendingUsage()
    {
        foreach (var kvp in _budgets)
        {
            var budget = kvp.Value;
            lock (budget.Lock)
            {
                // Only return budgets with non-zero deltas
                if (budget.ConsumedBandwidthDelta > 0 || budget.ConsumedTransformsDelta > 0)
                {
                    yield return (budget.TenantId, budget.PeriodId);
                }
            }
        }
    }

    private TenantBudget GetOrCreateBudget(Guid tenantId, Guid periodId)
    {
        return _budgets.GetOrAdd((tenantId, periodId), _ => new TenantBudget
        {
            TenantId = tenantId,
            PeriodId = periodId,
            RemainingBandwidthBytes = 0,
            RemainingTransforms = 0,
            GrantedBandwidthBytes = 0,
            GrantedTransforms = 0,
            LeaseId = Guid.Empty,
            ExpiresAt = DateTimeOffset.MinValue, // Expired by default
            RefillInProgress = false,
            LastRefillAttemptAt = DateTimeOffset.MinValue,
            ConsumedBandwidthDelta = 0,
            ConsumedTransformsDelta = 0
        });
    }
}
