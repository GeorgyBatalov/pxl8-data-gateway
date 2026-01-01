using Pxl8.DataGateway.Contracts.V1.PolicySnapshot;

namespace Pxl8.DataGateway.Services;

/// <summary>
/// In-memory policy snapshot cache (hot path, zero DB calls)
/// </summary>
public class PolicySnapshotCache : IPolicySnapshotCache, IDisposable
{
    private readonly ReaderWriterLockSlim _lock = new();
    private readonly ILogger<PolicySnapshotCache> _logger;

    // Current snapshot state
    private PolicySnapshotDto? _currentSnapshot;
    private DateTimeOffset _lastUpdateAt = DateTimeOffset.MinValue;
    private Dictionary<Guid, TenantPolicyDto> _tenantPolicies = new();

    public PolicySnapshotCache(ILogger<PolicySnapshotCache> logger)
    {
        _logger = logger;
    }

    public void UpdateSnapshot(PolicySnapshotDto snapshot)
    {
        _lock.EnterWriteLock();
        try
        {
            _currentSnapshot = snapshot;
            _lastUpdateAt = DateTimeOffset.UtcNow;

            // Rebuild tenant policies dictionary for fast lookup
            _tenantPolicies = snapshot.Tenants.ToDictionary(t => t.TenantId);

            _logger.LogInformation(
                "Policy snapshot updated: snapshot_id={SnapshotId}, version={Version}, tenants={TenantCount}, generated_at={GeneratedAt}",
                snapshot.SnapshotId, snapshot.Version, snapshot.Tenants.Count, snapshot.GeneratedAt);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public TenantPolicyDto? GetTenantPolicy(Guid tenantId)
    {
        _lock.EnterReadLock();
        try
        {
            return _tenantPolicies.TryGetValue(tenantId, out var policy) ? policy : null;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public TimeSpan GetSnapshotAge()
    {
        _lock.EnterReadLock();
        try
        {
            if (_lastUpdateAt == DateTimeOffset.MinValue)
            {
                return TimeSpan.MaxValue; // Never updated
            }

            return DateTimeOffset.UtcNow - _lastUpdateAt;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public Guid? GetSnapshotId()
    {
        _lock.EnterReadLock();
        try
        {
            return _currentSnapshot?.SnapshotId;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public PolicySnapshotDto? GetCurrentSnapshot()
    {
        _lock.EnterReadLock();
        try
        {
            return _currentSnapshot;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public void Dispose()
    {
        _lock?.Dispose();
    }
}
