# MultilayerCache Layer-Level Autonomy & Roadmap

This document describes how each layer in `MultilayerCacheManager` can make autonomous decisions while the manager handles global orchestration.

---

## 🔹 Smarter Refresh Strategies (Per Layer)

- Adaptive TTLs per layer
  - L1 (in-memory) short TTL, high eviction sensitivity
  - L2 (Redis) longer TTL, allows slightly stale data
- Layer-local jittered refresh to prevent synchronized refresh storms
- Stale-while-revalidate per layer
- Priority-based refresh: hot keys refreshed aggressively in fast layers

---

## 🔹 Background Refresh

- Layers may have optional background refresher
- Refresh batching per layer to reduce backend load

---

## 🔹 Layer-Level Observability

- Metrics per layer: hits/misses, eviction counts, load latency, early refresh counts
- Expose via Prometheus/OpenTelemetry
- Export raw snapshots: `_frequencyTracker`, `_hitsPerKey`, `_missesPerKey`

---

## 🔹 Layer-Specific Failure Handling

- Circuit breaker and fallback values per layer
- Retry/backoff per layer
- Dead-letter logging for persistent layers

---

## 🔹 Key Grouping and Bulk Operations

- Bulk loaders per layer
- Tagging / key groups handled per layer
- Hierarchical key support per layer for selective eviction

---

## 🔹 Multi-Writers and Write-Behind

- Each layer defines write policy: WriteThrough, WriteBehind
- L1 writes to L2; L2 writes to persistence
- Deduplication handled per layer

---

## 🔹 Pluggable Policies Per Layer

- TTL, refresh intervals, eviction, loader/writer selection configurable per layer
- Dynamic policy updates per layer
- Layer-local promotion rules using TinyLFU/Bloom filter checks

---

## 🔹 Hot Key Protection Per Layer

- Independent per-key refresh throttling
- L1 may enforce semaphores, L2 uses coarse-grained limits
- Hot key detection using `_accessCounts` or frequency sketch

---

## 🔹 Distributed Coordination

- Global coordination via manager, layers enforce local eviction/admission
- L1 layers admit/reject based on TinyLFU independently
- Metrics aggregation for global top-N keys combines layer-local stats

---

## 🔹 Developer Ergonomics

- Higher-level APIs remain manager-facing
- Per-layer inspection/decorator APIs:
  - `layer.GetTopKeys()`
  - `layer.GetFrequencyEstimate(key)`
  - `layer.AdjustTtl(key, ttl)`

---

## 🔹 Advanced Analytics & Insights

- Layer-level metrics: hit/miss ratios, eviction rates, frequency skew
- Layer-local anomaly detection for sudden access spikes
- Combine per-layer insights to tune global policies

---

## 🔹 Security & Reliability

- Layers may encrypt stored values independently
- Rate limiting enforced per layer
- Layer-level cleanup ensures no memory leaks (`_inflight`, `_keyLocks`, `_accessCounts`)

---

## ✅ Summary

- Manager orchestrates global behavior: request coalescing, promotion, global metrics
- Each layer enforces TTL, admission (TinyLFU/Bloom filter), eviction, refresh, and local metrics autonomously
- Ensures scalability, flexibility, and safe experimentation per layer

---

## 🔹 ASCII Diagram

```
        +--------------------------+
        |  MultilayerCacheManager  |
        |--------------------------|
        |  - Request Coalescing     |
        |  - Global Metrics         |
        |  - Promotion Policies     |
        +-----------+--------------+
                    |
        +-----------v--------------+
        |         Layer L1          |  <-- InMemoryCache / TinyLFU
        |--------------------------|
        |  - TTL, Jitter, Refresh  |
        |  - Admission (TinyLFU)   |
        |  - Eviction               |
        |  - Local Metrics          |
        +-----------+--------------+
                    |
        +-----------v--------------+
        |         Layer L2          |  <-- Redis / Persistent
        |--------------------------|
        |  - TTL, Jitter, Refresh  |
        |  - Admission (optional)  |
        |  - Eviction               |
        |  - Local Metrics          |
        +--------------------------+
```

- Each layer makes its own decisions; manager coordinates requests and aggregates metrics.
- TinyLFU/Bloom filters can be applied per layer for smarter admission.
- Early refresh, semaphores, and throttling applied locally per layer.