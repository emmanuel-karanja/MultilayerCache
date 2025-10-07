### TinyLFU and Bloom Filters Explained

This document explains the principles behind TinyLFU caching and Bloom filters, how they are used together, and provides ASCII diagrams for clarity.

---

## 1. Bloom Filter

**Purpose:**
A Bloom filter is a probabilistic data structure used to test whether an element is in a set. It can have false positives but never false negatives.

**Key Points:**
- Very memory efficient.
- Provides fast membership queries.
- Uses multiple hash functions.

**Structure:**
```
Bit array: [0, 0, 0, 0, 0, 0, 0, 0, 0, 0]
Hash functions: h1(), h2(), h3()
```

**Operation:**
1. **Add(key)**:
```
h1(key) -> 3
h2(key) -> 7
h3(key) -> 1
Set bits at indices 1, 3, 7 to 1
Bit array: [0, 1, 0, 1, 0, 0, 0, 1, 0, 0]
```
2. **Check(key)**:
```
h1(key) -> 3
h2(key) -> 7
h3(key) -> 1
All bits 1? -> Yes (key probably exists)
```

**Diagram:**
```
key "A" -> h1/h2/h3 -> indices 1,3,7 -> set bits
Query "B" -> h1/h2/h3 -> indices 2,3,5 -> bit 2 is 0 -> definitely not in set
``` 

**Notes:**
- Cannot remove items (without a counting variant).
- False positives possible.

---

## 2. TinyLFU (Tiny Least Frequently Used)

**Purpose:**
TinyLFU is a cache admission policy that decides whether a new item should be admitted to a cache based on approximate frequency of access.

**Key Concepts:**
- Tracks approximate frequency counts using a **Count-Min Sketch**.
- Uses a small sample to compare incoming item's frequency vs existing items.
- Helps high hit ratio caches by not polluting with low-frequency items.

**Components:**
- **Count-Min Sketch:** probabilistic frequency counter.
- **Admission Policy:** new item is admitted if its estimated frequency >= victim's frequency.

**Flow Diagram:**
```
New Item -> Check TinyLFU ->
   Frequency(New) >= Frequency(Victim)?
        Yes -> Admit (Evict victim)
        No  -> Reject
```

**Count-Min Sketch Example:**
```
Depth=2, Width=5
+---+---+---+---+---+
|   |   |   |   |   |
|   |   |   |   |   |  <- hash functions increment counts
+---+---+---+---+---+
Item "A" -> increment counters
Estimate("A") -> min(counter row1, row2)
```

**Benefits:**
- Efficient memory usage.
- Quickly adapts to workload.
- Reduces cache pollution.

---

## 3. Combining Bloom Filter + TinyLFU

**Purpose:**
- Bloom filter acts as a fast negative cache to filter out never-before-seen keys.
- TinyLFU decides whether frequent-but-new keys should evict existing cache entries.

**High-Level Flow:**
```
Client Request -> Bloom Filter Check
   |-- Not in Bloom Filter -> Miss -> Update frequency -> Load -> Possibly admit via TinyLFU
   |-- In Bloom Filter     -> Possibly in cache -> Check cache
```

**ASCII Diagram:**
```
     +------------------+
     |   Client Request  |
     +---------+--------+
               |
               v
     +------------------+
     |   Bloom Filter    |
     +---------+--------+
       No       | Yes
       |        v
       |   +-------------+
       |   | Check Cache |
       |   +-------------+
       |        |
       |   Hit  |  Miss
       |        | 
       v        v
 Load from DB   +----------------+
 (or loader)   | TinyLFU Admit? |
               +----------------+
               | Yes   |  No
               |       v
               v   Reject
        Store in Cache
```

**Key Takeaways:**
- Bloom filter helps avoid unnecessary cache lookups.
- TinyLFU ensures high-frequency items stay in cache.
- Combination improves overall cache hit ratio and reduces memory waste.

---

## References:
- [TinyLFU: A Highly Efficient Cache Admission Policy](https://arxiv.org/abs/1512.00727)
- [Bloom Filter on Wikipedia](https://en.wikipedia.org/wiki/Bloom_filter)
- Count-Min Sketch: Used for approximate frequency counting in TinyLFU