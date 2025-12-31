# PXL8 Data Gateway

**Version:** v1.0.0
**Target:** .NET 9.0
**Purpose:** High-performance image delivery plane (Data Plane)

---

## 🎯 Mission

**Zero database calls in hot path.** Autonomous operation for 10+ minutes without Control Plane.

---

## 🏗️ Architecture Principles

### Data Plane Characteristics:
1. **Hot Path = In-Memory Only**
   - No synchronous calls to PostgreSQL
   - No synchronous calls to Control Plane
   - Policy Snapshot cache (TTL 60s)
   - Budget state in-memory (TenantBudget)

2. **Asynchronous Communication**
   - Policy snapshots: Pull every 60s from Control Plane
   - Budget allocation: Request when needed, cache TTL leases
   - Usage reporting: Batch reports every 10-30s

3. **Autonomous Operation**
   - Cached policies last 10+ minutes
   - Budget leases have 5-minute TTL
   - Graceful degradation on Control Plane failure

---

## 📦 What's Inside

```
Pxl8.DataGateway/
├── Controllers/
│   └── ImagesController.cs        # Image upload/transform/delivery (hot path)
├── Services/
│   ├── BudgetManager.cs           # In-memory budget tracking
│   ├── PolicySnapshotCache.cs     # Tenant policies cache
│   ├── UsageAccumulator.cs        # Batch usage accumulator
│   └── ImageProcessor.cs          # Image transformation logic
├── BackgroundServices/
│   ├── PolicySnapshotSyncer.cs    # Pull snapshots every 60s
│   ├── UsageReporter.cs           # Push reports every 10-30s
│   └── BudgetRefiller.cs          # Request budget when low
└── Observability/
    ├── Metrics.cs                 # Prometheus metrics
    └── HealthChecks.cs            # Readiness/liveness probes
```

---

## 🔥 Hot Path Guarantee

**ImagesController endpoints:**
- `POST /api/v1/images` - Upload
- `GET /api/v1/images/{id}` - Deliver original
- `GET /api/v1/images/{id}/transform` - Deliver transformed

**Hot Path Requirements:**
- ✅ 0 database queries
- ✅ 0 synchronous HTTP calls to Control Plane
- ✅ <100ms p99 latency
- ✅ 1000+ RPS sustained
- ✅ Budget check in-memory only (TenantBudget state)

---

## 🔄 Background Processes

### PolicySnapshotSyncer (every 60s)
```csharp
GET https://control-api/internal/policy-snapshot
→ Update in-memory PolicyCache
```

### BudgetRefiller (when budget < 20%)
```csharp
POST https://control-api/internal/budget/allocate
{ tenant_id, period_id, bandwidth_requested, transforms_requested }
→ Update TenantBudget state (5-minute lease)
```

### UsageReporter (every 10-30s)
```csharp
POST https://control-api/internal/usage/report
{ report_id, tenant_id, period_id, bandwidth_used, transforms_used }
→ Idempotent by report_id
```

---

## 🛡️ Safety Invariants

1. **No Operations Without Budget**: If `TenantBudget.RemainingBandwidth == 0` → 429 Too Many Requests
2. **Suspended Tenant**: If `PolicySnapshot.Status != Active` → 403 Forbidden
3. **Expired Lease**: If `TenantBudget.ExpiresAt < Now` → budget = 0 (hard cutoff)
4. **Domain Verification**: Only serve images for verified domains
5. **Control Plane Down**: Use cached policies, serve traffic for 10+ min

---

## 📊 Observability

### Prometheus Metrics:
- `pxl8_data_requests_total{tenant_id,endpoint,status}`
- `pxl8_data_bandwidth_bytes{tenant_id,direction}`
- `pxl8_data_transform_duration_seconds{percentile}`
- `pxl8_data_budget_remaining_bytes{tenant_id}`
- `pxl8_data_policy_sync_age_seconds`

### Health Checks:
- `/health/live` - Container alive?
- `/health/ready` - Ready to serve traffic? (policy cache fresh, budget manager OK)

---

## 🚀 Deployment

**Target:** Yandex Cloud (Russia)
**Infrastructure:**
- Compute: Yandex Cloud Functions / Serverless Containers
- Storage: S3-compatible Object Storage
- Cache: Yandex Cloud Redis
- CDN: Yandex CDN (optional)

**Environment Variables:**
```bash
CONTROL_API_URL=https://control-api.pxl8.io
DATAPLANE_ID=ru-central1-a
POLICY_SYNC_INTERVAL=60000  # 60 seconds
USAGE_FLUSH_INTERVAL=10000  # 10 seconds
BUDGET_REFILL_THRESHOLD=0.2 # 20% of granted
```

---

## 📝 Related Documents

- [ARCHITECTURE_SPLIT.md](../ARCHITECTURE_SPLIT.md) - Split plane architecture
- [BUDGET_ALGORITHM.md](../BUDGET_ALGORITHM.md) - Budget allocation algorithm
- [ROADMAP.md](../ROADMAP.md) - Implementation roadmap

---

**Last Updated:** 31 December 2024 (v1.0.0)
