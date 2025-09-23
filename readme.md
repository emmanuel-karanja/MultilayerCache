## Approaching Caching with PICUS

Caching is critical for improving performance and scalability in modern systems. To approach caching systematically, we can use the PICUS framework:

### **P – Placement**
**Definition:** Where the cache resides in the architecture.

**Considerations:**
- **Local (L1) vs Distributed (L2):** 
  - L1: in-memory, per-instance cache (fastest, volatile)
  - L2: Redis, Memcached (shared across instances, slightly slower)
- **Client-side caching:** Useful for frequently accessed read-heavy data.
- **CDN caching:** For static assets or API responses.

**Policies:**
- Determine cache location based on latency sensitivity.
- Use multi-level caches (e.g., L1 + L2) for optimal performance.

---

### **I – Invalidation / Eviction**
**Definition:** How and when cached items are removed.

**Considerations:**
- **Time-based:** TTL (Time-to-Live) or expiry.
- **Space-based:** LRU (Least Recently Used), LFU (Least Frequently Used).
- **Event-driven:** Invalidate or delete cache entries on DB changes.

**Policies:**
- Use TTL for transient data.
- Use LRU/LFU for limited memory scenarios.
- Event-driven invalidation ensures cache consistency after writes.

---

### **C – Consistency**
**Definition:** How cache and DB stay in sync.

**Considerations:**
- **Strong consistency:** Write-through cache, where writes go to both cache and DB synchronously.
- **Eventual consistency:** Write to DB first, then update or invalidate cache asynchronously via events.
- **Stale-while-revalidate:** Serve slightly stale data while refreshing cache in background.

**Policies:**
- Critical data: prefer write-through for immediate consistency.
- Less critical or high-throughput scenarios: eventual consistency with events.
- Avoid overloading the DB with simultaneous cache rebuilds.

---

### **U – Update / Refresh**
**Definition:** How cached data is updated proactively.

**Considerations:**
- **Write-behind / Asynchronous update:** Cache updated first, DB flushed later (rare, risky).
- **Scheduled refresh:** Background jobs periodically refresh cache.
- **Event-driven refresh:** Cache updated or repopulated after DB write event (as in your multilayer cache scenario).

**Policies:**
- Use background refresh for read-heavy, slowly changing data.
- Use event-driven refresh for DB writes that require cache to stay fresh.
- Avoid overloading cache with refresh storms.

---

### **S – Scaling**
**Definition:** How caching scales with traffic and data.

**Considerations:**
- **Horizontal scaling:** Multiple cache nodes, sharding, consistent hashing.
- **Vertical scaling:** Increase memory or compute resources.
- **Replication and high availability:** Ensure cache survives node failures.

**Policies and Implementation for Transparent Scaling:**
- **Redis:** Use **cluster mode** to partition data across multiple nodes with automatic sharding. Enable **replication** for high availability and **sentinel** for failover. Redis clients handle cluster node selection, making scaling transparent to applications.
- **Memcached:** Use **consistent hashing** to distribute keys across multiple cache nodes. Adding or removing nodes only affects a minimal subset of keys. Most Memcached clients handle key distribution automatically, providing transparent scaling.
- Monitor cache metrics and scale nodes dynamically based on load and memory usage to maintain performance.

---

### **Summary: PICUS in Practice**
- **Placement:** L1 in-memory + L2 Redis/Memcached for multilayer cache.
- **Invalidation:** Event-driven on DB updates + TTL for memory cleanup.
- **Consistency:** Eventual consistency via DB -> Event -> Cache update.
- **Update:** Loader function handles cache miss and refresh.
- **Scaling:** Consistent hashing, Redis clustering, replication, and Memcached node management provide transparent horizontal scaling.

This approach ensures that your caching strategy is **systematic, scalable, and maintainable**, minimizing stale data while maximizing performance.
# MultilayerCache

**MultilayerCache** is a .NET solution implementing the PICUS multi-layer caching system using both in-memory and Redis backends with Protocol Buffers serialization. This project includes automation scripts for dependency setup, .proto compilation, build, test, and Docker integration.

---

## Table of Contents

* [Features](#features)
* [Project Structure](#project-structure)
* [Prerequisites](#prerequisites)
* [Setup](#setup)
* [Running the Solution](#running-the-solution)
* [Testing](#testing)
* [Docker](#docker)
* [Protobuf Compilation](#protobuf-compilation)
* [License](#license)

---

## Features

* Multi-layer caching with in-memory and Redis.
* Serialization using Protocol Buffers (Google.Protobuf).
* Unit tests with xUnit.
* Automated build, restore, and Docker scripts.

---

## Project Structure

```
MultilayerCache/
├── MultilayerCache/        # Core caching library
│   ├── Cache/
│   │   ├── CacheItem.cs
│   │   ├── CacheMetrics.cs
│   │   ├── ICache.cs
│   │   ├── InMemoryCache.cs
│   │   ├── MultilayerCacheManager.cs
│   │   ├── ProtobufSerializer.cs
│   │   ├── RedisCache.cs
│   │   
│   └── Protos/             # Protocol Buffers .proto files
├── MultilayerCache.Demo/   # Demo project
├── MultilayerCache.Tests/  # Unit tests
└── Run_MultilayerCache.ps1 # Automation script
```

---

## Prerequisites

* .NET SDK 8.0 or later
* PowerShell 7+
* Docker (optional for containerized run)
* Redis (local or remote)

---

## Manual Setup

1. Clone the repository:

```powershell
git clone https://github.com/emmanuel-karanja/MultilayerCache.git
cd MultilayerCache
```

2. Restore NuGet packages:

```powershell
dotnet restore MultilayerCache.sln
```

3. Install `protoc` (Protocol Buffers compiler) if missing.

---

## Setup with Automation

``` powershell

git clone https://github.com/emmanuel-karanja/MultilayerCache.git
cd MultilayerCache

./Build.ps1 -Clean
./Run-Demo.ps1
./Run-MetricsDemo.ps1

By default, all three options are run i.e. clean, build, docker image creation and running.

```

## Running the Solution

Use the automation script `Run_MultilayerCache.ps1`:

```powershell
# Clean, build, run tests, and Docker
.\Run_MultilayerCache.ps1

# Options:
.\Run_MultilayerCache.ps1 -Clean       # Clean bin/obj folders
.\Run_MultilayerCache.ps1 -BuildOnly   # Build only
.\Run_MultilayerCache.ps1 -RunOnly     # Run Docker only
```

---

## Testing

Unit tests are included in `MultilayerCache.Tests`. Run tests with:

```powershell
dotnet test MultilayerCache.Tests/MultilayerCache.Tests.csproj -c Release
```

Verbose output can be enabled in the tests for detailed cache hit/miss information.

---

## Docker

Build and run containers using Docker Compose:

```powershell
docker-compose -f docker-compose.yml up --build
```

---

## Protobuf Compilation

**Compile `.proto` files to C# classes**:

```powershell
# Ensure protoc is installed
$protocUrl = "https://github.com/protocolbuffers/protobuf/releases/download/v23.5/protoc-23.5-win64.zip"
Invoke-WebRequest -Uri $protocUrl -OutFile "$env:TEMP\protoc.zip"
Expand-Archive -Path "$env:TEMP\protoc.zip" -DestinationPath "$env:USERPROFILE\protoc" -Force
$env:PATH += ";$env:USERPROFILE\protoc\bin"

# Compile proto files
protoc --csharp_out=MultilayerCache/Cache --proto_path=MultilayerCache/Protos MultilayerCache/Protos/*.proto
```

> The generated C# classes (e.g., `User.cs`) should reside in `MultilayerCache/Cache`.

---

## License

This project is licensed under the MIT License.
