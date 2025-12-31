namespace Pxl8.DataGateway.Models;

/// <summary>
/// In-memory budget state for a single tenant
/// </summary>
/// <remarks>
/// ONE instance per (tenant_id, period_id) tuple in Data Plane
/// Updated by: BudgetManager (on lease grant, on spend)
/// Read by: Hot path (ImagesController - budget checks)
/// Spec: BUDGET_ALGORITHM.md v1.1
/// </remarks>
public class TenantBudget
{
    /// <summary>
    /// Tenant unique identifier
    /// </summary>
    public required Guid TenantId { get; set; }

    /// <summary>
    /// Billing period GUID (NOT "YYYY-MM")
    /// </summary>
    public required Guid PeriodId { get; set; }

    // ========== Budget Balances (Current State) ==========

    /// <summary>
    /// Remaining bandwidth in current lease (bytes)
    /// </summary>
    /// <remarks>
    /// Hot path checks: if RemainingBandwidthBytes < bytes_needed → 429
    /// Decremented on every upload/download/transform
    /// </remarks>
    public long RemainingBandwidthBytes { get; set; }

    /// <summary>
    /// Remaining transforms in current lease (count)
    /// </summary>
    public int RemainingTransforms { get; set; }

    // ========== Granted Amounts (For Threshold Calculation) ==========

    /// <summary>
    /// Total bandwidth granted in current lease (bytes)
    /// </summary>
    /// <remarks>
    /// Used for refill threshold: trigger refill when remaining < granted * 0.2
    /// </remarks>
    public long GrantedBandwidthBytes { get; set; }

    /// <summary>
    /// Total transforms granted in current lease (count)
    /// </summary>
    public int GrantedTransforms { get; set; }

    // ========== Lease Metadata ==========

    /// <summary>
    /// Current budget lease ID (from Control Plane)
    /// </summary>
    public Guid LeaseId { get; set; }

    /// <summary>
    /// Lease expiry timestamp (UTC)
    /// </summary>
    /// <remarks>
    /// CRITICAL: If DateTimeOffset.UtcNow > ExpiresAt → RemainingBandwidth = 0 (hard cutoff)
    /// </remarks>
    public DateTimeOffset ExpiresAt { get; set; }

    // ========== Refill Control ==========

    /// <summary>
    /// Flag: refill request in progress (prevents duplicate requests)
    /// </summary>
    public bool RefillInProgress { get; set; }

    /// <summary>
    /// Timestamp of last refill attempt (for cooldown)
    /// </summary>
    /// <remarks>
    /// Cooldown: don't retry refill if last attempt < 10 seconds ago
    /// </remarks>
    public DateTimeOffset LastRefillAttemptAt { get; set; }

    // ========== Local Accumulation (For Usage Reports) ==========

    /// <summary>
    /// Bandwidth consumed since last usage report (bytes)
    /// </summary>
    /// <remarks>
    /// Delta accumulator. Reset to 0 after successful usage report.
    /// Flush interval: 10-30 seconds
    /// </remarks>
    public long ConsumedBandwidthDelta { get; set; }

    /// <summary>
    /// Transforms consumed since last usage report (count)
    /// </summary>
    public int ConsumedTransformsDelta { get; set; }

    /// <summary>
    /// Thread-safe lock for budget operations
    /// </summary>
    public object Lock { get; } = new object();
}
