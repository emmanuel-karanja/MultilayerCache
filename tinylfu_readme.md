### Canvas Doc: EnhancedTinyLFUInMemoryCache with Multi-layer Integration

---

#### Overview
The `EnhancedTinyLFUInMemoryCache` is a high-performance, in-memory L1 cache supporting:

- TinyLFU admission policy
- Optional early refresh / soft TTL
- Frequency tracking with Count-Min Sketch
- Eviction via sampled TinyLFU
- Full per-key and global metrics
- Multi-layer promotion (e.g., L1 → Redis L2)
- Async support
- Periodic cleanup of expired items
- Frequency decay to prioritize recent accesses

It integrates naturally with an L2 cache like Redis.

---

#### Core Features

| Feature | Description |
|---------|-------------|
| TinyLFU Admission | When the cache reaches `_maxSize`, keys are admitted based on their estimated frequency using Count-Min Sketch. |
| Frequency Decay | Periodic halving of all frequency counts to adapt to recent access patterns. |
| Early Refresh | Detects items nearing TTL and increments early refresh counters; supports background refresh. |
| Metrics | Tracks hits, misses, last latency, promotion count, early refresh, last refresh timestamp, and in-flight keys. |
| Eviction | Uses TinyLFU sampling: pick N random keys and evict the one with the lowest frequency. |
| Promotion | Supports L2 → L1 promotions (`PromoteFromLowerLayer`) with remaining TTL. |
| Async Operations | Implements `SetAsync` / `TryGetAsync` to support async multi-layer flows. |
| Decay Timer | Optional decay interval ensures recent keys dominate eviction decisions. |

---

#### Cache Structure (ASCII)

```
+-------------------+        +----------------+
|  L1 Enhanced      |        |  L2 Redis      |
|  TinyLFU Cache    | <----> |  LFU/other     |
|                   |        |                |
|  _cache           |        |  _db           |
|  CountMinSketch   |        |  Approx LFU    |
|  Metrics          |        |  Metrics       |
+-------------------+        +----------------+
```

- L1 is memory-fast and supports admission policies.
- L2 is Redis (networked) with optional approximate LFU eviction.
- Keys can promote from L2 → L1 when accessed frequently.

---

#### Flow: Set Operation

```
Caller
   |
   v
Set(key, value, ttl)
   |
   +--> Increment frequency (Count-Min Sketch)
   |
   +--> If L1 full and TinyLFU enabled
           |
           +--> Sample N random keys
           |
           +--> Identify lowest frequency victim
           |
           +--> Reject key or evict victim
   |
   +--> Store in _cache with TTL
   |
   +--> Update metrics (promotion, latency, last refresh)
```

---

#### Flow: TryGet Operation

```
Caller
   |
   v
TryGet(key)
   |
   +--> Increment frequency (Count-Min Sketch)
   |
   +--> Check _cache
         |
         +--> Hit:
         |       - Update metrics (hits, latency)
         |       - Check early refresh threshold
         |       - Return value
         |
         +--> Miss:
                 - Update metrics (misses)
                 - Return default
```

---

#### Flow: Promote from L2

```
Caller / L2 read hit
   |
   v
PromoteFromLowerLayer(key, value, remainingTtl)
   |
   +--> Store in L1 _cache
   +--> Update promotion metrics
```

---

#### Flow: Eviction via TinyLFU

```
L1 Cache full?
   |
   v
Sample N keys randomly
   |
   v
Compute Count-Min Sketch frequency for each
   |
   v
Evict key with lowest frequency
```

- `IdentifyVictim()` handles sampling + eviction.
- Sampling prevents expensive global scans.

---

#### Flow: Frequency Decay

```
Every decay interval (default 5 min):
   |
   v
_countMinSketch.Decay()
   |
   +--> Halves all counters
   +--> Recent accesses dominate eviction
```

---

#### Metrics Snapshot Example

```
HitsPerKey: { k1: 10, k2: 3 }
MissesPerKey: { k1: 2, k3: 5 }
LastLatencyPerKey: { k1: 0.05ms, k2: 0.07ms }
PromotionCountPerKey: { k1: 3, k2: 1 }
EarlyRefreshCountPerKey: { k1: 2 }
LastRefreshTimestamp: { k1: 10:15:01, k2: 10:16:20 }
InFlightKeys: { k3 }
TotalHits: 13
TotalMisses: 7
TotalPromotions: 4
TotalEarlyRefreshes: 2
TopKeysByAccessCount: [ k1, k2, k3 ]
```

---

#### Integration with Redis L2

1. L1 handles all fast hits, early refresh, and TinyLFU admission.
2. On miss:
   - MultilayerCacheManager calls L2 Redis → if hit, promote to L1 (`PromoteFromLowerLayer`).
   - If L2 miss → fetch from loader → write to L1 + L2 via write policy.
3. Metrics are tracked per layer + global.
4. Redis may optionally use approximate LFU for its own eviction; L1 TinyLFU ensures memory layer optimization.

```
Client
  |
  v
+--------+
|  L1    |---> if miss, promote from L2
+--------+
  |
  v
+--------+
|  L2    |---> if miss, load from DB
+--------+
```

---

✅ Summary
- **TinyLFUInMemoryCache** is fully featured for L1 caching:
  - TinyLFU admission
  - Frequency decay
  - Metrics & early refresh
  - Multi-layer promotion
- Integrates seamlessly with Redis (L2) which has approximate LFU.
- Multi-layer architecture reduces network hits and optimizes hot key performance.