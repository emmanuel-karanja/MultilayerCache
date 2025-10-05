
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

* Multi-layer caching with in-memory and Redis backends.
* Support for **per-layer TTLs** for fine-grained cache expiration control.
* **Write policies**: Write-Through and Write-Behind for flexible cache writes.
* Serialization using **Protocol Buffers** (`Google.Protobuf`).
* Delegated **loader and persistent store writer functions** for flexible data sources.
* **Telemetry hooks** with OpenTelemetry for tracing and metrics.
* **Instrumentation** with cache metrics and logging.
* Unit tests with **xUnit**.
* Automated build, restore, Docker scripts, and proto compilation.

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

## Telemetry Hooks

**MultilayerCache** integrates with **OpenTelemetry** to provide tracing and metrics for cache operations. This allows you to monitor cache performance, hits/misses, and write operations in real time.

### Features

* **Traces**: Capture cache hits, misses, loader calls, and write operations.
* **Metrics**: Record counters for cache hits, misses, refreshes, and background writes.
* **Custom instrumentation**: Use `InstrumentedCacheManagerDecorator` to automatically add telemetry around `MultilayerCacheManager` operations.
* **Integration**: Compatible with ASP.NET Core applications using `AddOpenTelemetry()` for centralized tracing and metric collection.
* **Exporter support**: Console, Prometheus, or other OpenTelemetry exporters can be configured.

### Usage Example

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry()
    .WithTracing(tracerProviderBuilder =>
    {
        tracerProviderBuilder
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddSource("MultilayerCache.Demo")
            .AddConsoleExporter();
    })
    .WithMetrics(metricsProviderBuilder =>
    {
        metricsProviderBuilder
            .AddMeter("MultilayerCache.Instrumentation")
            .AddRuntimeInstrumentation()
            .AddHttpClientInstrumentation()
            .AddConsoleExporter();
    });

// Decorate cache manager with telemetry
builder.Services.AddSingleton<InstrumentedCacheManagerDecorator<string, User>>(sp =>
{
    var baseCache = sp.GetRequiredService<MultilayerCacheManager<string, User>>();
    return new InstrumentedCacheManagerDecorator<string, User>(baseCache);
});
```

## Soft TTL (Early Refresh) Implementation

MultilayerCache supports **Soft TTL**, meaning cached values are returned immediately even if they are near expiration, while a **background refresh** ensures the cache stays up-to-date.

---

### How to Use Soft TTL

When you configure a `MultilayerCacheManager`, you can set:

- `defaultTtl`: the main TTL for cached items.
- `earlyRefreshThreshold`: the "soft TTL window" before the value is considered stale and a refresh is triggered.
- `minRefreshInterval`: minimum interval between background refreshes per key.
- `maxConcurrentEarlyRefreshes`: global concurrency limit for background refresh tasks.

Example:

```csharp
var cacheManager = new MultilayerCacheManager<string, User>(
    layers: new ICache<string, User>[] { memoryCache, redisCache },
    loaderFunction: async (key, ct) => await LoadUserFromDbAsync(key),
    logger: logger,
    defaultTtl: TimeSpan.FromMinutes(5),
    earlyRefreshThreshold: TimeSpan.FromMinutes(1),
    minRefreshInterval: TimeSpan.FromSeconds(30),
    maxConcurrentEarlyRefreshes: 5
);

// Optional: hook telemetry for early refresh events
cacheManager.OnEarlyRefresh = key => logger.LogInformation("Early refresh triggered for key {Key}", key);

```

# MultilayerCache – Concurrency and Latency Details

## Semaphore Roles in GetOrAddAsync

### 1. `_inflight` Dictionary with `Lazy<Task<TValue>>`

* Ensures only one loader runs for a given key at a time.
* Other requests for the same key will wait on the same `Task` instead of launching duplicate loaders.

### 2. `_keyLocks`

* Optional fine-grained lock for ultra-hot keys.
* Ensures only one thread executes the actual loader call even if multiple arrive at the same moment.

### 3. `_earlyRefreshConcurrencySemaphore`

* Global limiter for background refresh tasks.
* Prevents system overload by capping concurrent refreshes.

---

## Why This Design Doesn’t Increase Latency

The `GetOrAddAsync` method remains fast because:

* **Cache hits** return immediately without waiting for refresh logic.
* Refresh logic is **triggered inline but executed asynchronously** using fire-and-forget tasks.
* The inline cost is trivial: time checks, last refresh comparisons, and at most a non-blocking `TryEnter` on the global semaphore.
* These operations are **microsecond-level** and have negligible impact on request latency.

### Background Task Usage

* Background tasks are spawned only when conditions warrant (near TTL expiry, minimum refresh interval elapsed, concurrency slots available).
* They run **outside the caller’s execution path**, meaning the caller doesn’t pay for loader latency during a hit.
* This makes refresh **opportunistic**, keeping cache fresh without impacting response time.

---

## What this means for latency

**Cache Hit Path:**

* `GetOrAddAsync` returns immediately after a cache hit.
* Caller isn’t blocked waiting for refresh.
* Early refresh is fire-and-forget, only starting if conditions are right (close to TTL, min interval respected, concurrency slots free).
* Impact on latency: negligible.

  * Checks `_lastRefresh`
  * A couple of time comparisons
  * Maybe a `TryEnter` on the global semaphore
  * All microsecond-level overhead.

**Cold Miss Path:**

* Only case where caller waits is when **no cache layer has the value**.
* Even then, only the **first caller** pays the loader cost.
* Other callers piggyback on the in-flight `Task`.

---

## ASCII Timing Diagrams

### Cache Hit + Early Refresh

```
Client calls GetOrAddAsync("K") ──────────┐
                                          │
[Cache Hit]                               │
   │                                      │
   ▼                                      │
 Return value immediately ◄───────────────┘
   │
   │   In parallel...
   │
   └──► TriggerEarlyRefresh("K")
           │
           ├─ Checks TTL / interval
           │
           ├─ If eligible: starts Task.Run
           │       │
           │       └── Calls loader in background
           │             Updates cache layers
           │             Updates _lastRefresh
           │
           └─ If not eligible: no-op
```

### Cold Miss Path

```
Client calls GetOrAddAsync("K") ──────────┐
                                          │
[Cache Miss in all layers]                │
   │                                      │
   ▼                                      │
Check _inflight for key                   │
   │                                      │
   ├─ If not present: create Lazy<Task>   │
   │       │                              │
   │       └── Call loader (awaited)      │
   │             ▼                        │
   │          Value returned              │
   │          Cache updated               │
   │          _lastRefresh updated        │
   │                                      │
   └─ If present: await existing Task ◄───┘

Return value once loader finishes
```

**So:**

* Cache hits stay fast → user gets data instantly.
* Refresh is async and opportunistic → user never pays refresh cost.
* Cold misses are coordinated → only one caller per key pays loader cost.

## Loader and Writer Functions

The `MultilayerCache` uses **delegates** for both cache loading and persistence.  
This makes it flexible: you can pass in any function, lambda, or class method that matches the expected signature.

### Loader Functions
A **loader** is used when a cache miss occurs (or when a refresh is triggered).
- Must return `Task<TValue>`.
- Can be:
  - An `async` lambda:  
    ```csharp
    key => FetchFromDatabaseAsync(key)
    ```
  - A method group:  
    ```csharp
    MyRepository.LoadUserAsync
    ```
  - A static or instance method:
    ```csharp
    async Task<User> LoadUserAsync(string userId) { ... }
    ```

**Example:**
```csharp
var value = await cache.GetOrAddAsync(
    key: "user:123",
    loader: userId => userRepository.LoadUserAsync(userId)
);

```

## Future Improvements
[MultilayerCache Improvements & Roadmap](future_improvements.md)

## License

This project is licensed under the MIT License.
