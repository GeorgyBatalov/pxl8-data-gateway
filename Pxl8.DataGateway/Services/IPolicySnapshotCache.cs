using Pxl8.ControlApi.Contracts.V1.PolicySnapshot;

namespace Pxl8.DataGateway.Services;

/// <summary>
/// Policy snapshot cache - in-memory tenant policies (hot path, zero DB calls)
/// </summary>
public interface IPolicySnapshotCache
{
    /// <summary>
    /// Update snapshot (called by background worker every 60s)
    /// </summary>
    void UpdateSnapshot(PolicySnapshotDto snapshot);

    /// <summary>
    /// Get tenant policy by tenant ID (hot path - fast lookup)
    /// </summary>
    TenantPolicyDto? GetTenantPolicy(Guid tenantId);

    /// <summary>
    /// Get snapshot age (how long since last update)
    /// </summary>
    TimeSpan GetSnapshotAge();

    /// <summary>
    /// Get snapshot ID (for debugging/monitoring)
    /// </summary>
    Guid? GetSnapshotId();

    /// <summary>
    /// Get current snapshot (for health checks/monitoring)
    /// </summary>
    PolicySnapshotDto? GetCurrentSnapshot();
}
