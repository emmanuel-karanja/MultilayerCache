# MultilayerCache

**MultilayerCache** is a .NET solution implementing a multi-layer caching system using both in-memory and Redis backends with Protocol Buffers serialization. This project includes automation scripts for dependency setup, .proto compilation, build, test, and Docker integration.

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
* [GitHub Integration](#github-integration)
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

## Setup

1. Clone the repository:

```powershell
git clone https://github.com/EmmanuelNganga/MultilayerCache.git
cd MultilayerCache
```

2. Restore NuGet packages:

```powershell
dotnet restore MultilayerCache.sln
```

3. Install `protoc` (Protocol Buffers compiler) if missing.

---

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
