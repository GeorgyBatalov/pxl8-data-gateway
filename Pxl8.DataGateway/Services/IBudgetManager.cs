using Pxl8.ControlApi.Contracts.V1.BudgetAllocation;
using Pxl8.DataGateway.Models;

namespace Pxl8.DataGateway.Services;

/// <summary>
/// Budget manager - in-memory budget tracking (hot path, zero DB calls)
/// </summary>
public interface IBudgetManager
{
    /// <summary>
    /// Try to spend budget (hot path - 0 external calls)
    /// </summary>
    /// <returns>true if budget available and spent, false if insufficient</returns>
    bool TrySpendBandwidth(Guid tenantId, Guid periodId, long bytes);

    /// <summary>
    /// Try to spend transform quota (hot path - 0 external calls)
    /// </summary>
    bool TrySpendTransform(Guid tenantId, Guid periodId);

    /// <summary>
    /// Grant budget lease (from Control Plane allocation response)
    /// </summary>
    void GrantLease(Guid tenantId, Guid periodId, BudgetAllocateResponse lease);

    /// <summary>
    /// Check if budget should be refilled (< 20% of granted)
    /// </summary>
    bool ShouldRefill(Guid tenantId, Guid periodId);

    /// <summary>
    /// Get current budget state (for monitoring/debugging)
    /// </summary>
    TenantBudget? GetBudget(Guid tenantId, Guid periodId);

    /// <summary>
    /// Get consumed delta for usage reporting (and reset delta to 0)
    /// </summary>
    (long bandwidthDelta, int transformsDelta) GetAndResetConsumedDelta(Guid tenantId, Guid periodId);

    /// <summary>
    /// Get all tenant/period pairs with pending usage (non-zero deltas)
    /// </summary>
    IEnumerable<(Guid TenantId, Guid PeriodId)> GetAllTenantsWithPendingUsage();
}
