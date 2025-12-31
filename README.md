# PXL8 Data Gateway

> **Version:** 1.0.0
> **Status:** MVP Complete
> **Architecture:** Autonomous Data Plane (RU/Yandex Cloud)

---

## Overview

**Data Gateway** is the high-performance image delivery plane for PXL8. It operates **autonomously** for 10+ minutes without Control Plane, ensuring zero downtime and <100ms p99 latency.

### Key Principles:

- 🚀 **Hot Path Guarantee**: 0 database queries, 0 synchronous HTTP calls to Control Plane
- 💰 **Budget Enforcement**: In-memory token-based quota tracking (bandwidth + transforms)
- 📦 **Policy Snapshots**: Pull-based configuration sync (every 60 seconds)
- 📊 **Usage Reporting**: Push-based delta reporting (every 10 seconds)
- 🔄 **Budget Refill**: Automatic refill when < 20% remaining

---

## Hot Path Flow

### Image Request (GET /api/v1/images/{id})

```
User Request → ImagesController
              ↓
         BudgetManager.TrySpendBandwidth(tenant, period, bytes)
              ↓
         [In-Memory Check - 0 external calls]
              ↓
     ✅ Budget OK       ❌ Budget Exhausted
              ↓                   ↓
       Return Image         HTTP 429 Quota Exceeded
```

**Latency:** <1ms (pure in-memory operation)

---

## Background Services

### PolicySnapshotSyncer (60s interval)
Pull latest tenant policies from Control Plane

### UsageReporter (10s interval)  
Push usage deltas to Control Plane (idempotent by report_id)

### BudgetRefiller (5s check)
Request new budget when < 20% remaining

---

## Configuration

**appsettings.json:**
```json
{
  "DataPlane": {
    "DataPlaneId": "local-dev",
    "ControlApiUrl": "http://localhost:5100",
    "PolicySyncInterval": "00:01:00",
    "UsageFlushInterval": "00:00:10",
    "BudgetRefillCheckInterval": "00:00:05"
  }
}
```

---

## API Endpoints

### Public Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/v1/images/{id}` | Get original image (budget check) |
| GET | `/api/v1/images/{id}/transform` | Get transformed image |
| GET | `/api/v1/images/budget-status` | Debug: view budget state |

### Health Check Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/health/live` | Liveness probe |
| GET | `/health/ready` | Readiness probe |

---

## Autonomous Operation

**Data Gateway operates 10+ minutes without Control Plane:**

1. **Policy Snapshots:** Cached for 60s
2. **Budget Leases:** 5-minute TTL (hard cutoff)
3. **Usage Reports:** Accumulate in memory

**Lease Expiry:** At `ExpiresAt` → all budget = 0 → HTTP 429

---

## Development

**Build:**
```bash
cd data-gateway/Pxl8.DataGateway
dotnet build
```

**Run:**
```bash
dotnet run
```

---

## Architecture Docs

- [ARCHITECTURE_SPLIT.md](../ARCHITECTURE_SPLIT.md) - Split Planes v1.2
- [BUDGET_ALGORITHM.md](../BUDGET_ALGORITHM.md) - Budget Algorithm v1.1
- [EPIC_B_SPLIT_PLANES.md](../EPIC_B_SPLIT_PLANES.md) - Implementation Plan

---

## What's NOT Included (MVP Stub)

- ❌ Actual image storage/transformation
- ❌ Domain verification
- ❌ API key authentication
- ❌ HMAC security

To be added after validating split plane architecture.

---

**License:** Proprietary - PXL8 Project
