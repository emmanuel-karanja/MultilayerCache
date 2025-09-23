<#
.SYNOPSIS
    Cleans, builds, ensures Redis is running, stops previous instances, and runs MultilayerCache.Metrics.Demo.
.DESCRIPTION
    Restores dependencies, cleans and builds the MultilayerCache.Metrics.Demo, starts Redis via docker-compose,
    waits for Redis to be ready using container health status, stops any previously running instances,
    and runs the microservice via `dotnet run`.
#>

param (
    [string]$SolutionRoot = ".",
    [string]$Configuration = "Debug",        # Use Release if needed
    [string]$Runtime = "win-x64",            # Change to linux-x64 if on Linux
    [string]$DockerComposeFile = ".\docker-compose.yml",
    [int]$MaxRedisRetries = 10,
    [int]$RedisRetryDelaySeconds = 3
)

# Paths
$sampleProjectDir = Join-Path $SolutionRoot "MultilayerCache"
$sampleProjectFile = Join-Path $sampleProjectDir "MultilayerCache.Metrics.Demo.csproj"

# ----------------------------
# Step 0: Prerequisites
# ----------------------------
Write-Host "`n=== Checking prerequisites ==="

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "❌ .NET SDK not found. Please install .NET SDK to continue."
    exit 1
} else {
    Write-Host "✅ .NET SDK found."
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Write-Error "❌ Docker not found. Please install Docker to continue."
    exit 1
} else {
    Write-Host "✅ Docker CLI found."
}

# ----------------------------
# Step 1: Stop previous instances
# ----------------------------
Write-Host "`n=== Stopping previously running MultilayerCacheMetricsDemo instances ==="
$existingProcesses = Get-Process -Name "dotnet" -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path -match "MultilayerCache.Metrics.Demo.dll" }
if ($existingProcesses) {
    foreach ($proc in $existingProcesses) {
        Write-Host "Stopping process Id $($proc.Id)..."
        Stop-Process -Id $proc.Id -Force
    }
    Write-Host "✅ Previous instances stopped."
} else {
    Write-Host "No existing instances found."
}

# ----------------------------
# Step 2: Start Redis via docker-compose
# ----------------------------
Write-Host "`n=== Starting Redis via docker-compose ==="
if (-Not (Test-Path $DockerComposeFile)) {
    Write-Error "❌ docker-compose.yml not found at $DockerComposeFile"
    exit 1
}

docker-compose -f $DockerComposeFile up -d
if ($LASTEXITCODE -ne 0) {
    Write-Error "❌ Failed to start Redis via docker-compose."
    exit $LASTEXITCODE
} else {
    Write-Host "✅ Redis container started (detached)."
}

# ----------------------------
# Step 3: Wait for Redis to become healthy
# ----------------------------
Write-Host "`n=== Waiting for Redis container to be healthy ==="
$retry = 0
$redisReady = $false
$redisContainerName = "redis_cache"

while ($retry -lt $MaxRedisRetries -and -not $redisReady) {
    try {
        $status = (docker inspect --format="{{.State.Health.Status}}" $redisContainerName 2>$null).Trim()
        if ($status -eq "healthy") {
            $redisReady = $true
        } else {
            Write-Host "⏳ Attempt $($retry+1)/$MaxRedisRetries : Redis status = '$status'. Retrying..."
            Start-Sleep -Seconds $RedisRetryDelaySeconds
            $retry++
        }
    } catch {
        Write-Host "⏳ Redis container not ready yet. Retrying..."
        Start-Sleep -Seconds $RedisRetryDelaySeconds
        $retry++
    }
}

if (-not $redisReady) {
    Write-Warning "⚠ Redis did not become healthy after $MaxRedisRetries retries. Proceeding anyway..."
} else {
    Write-Host "✅ Redis is healthy and ready!"
}

# ----------------------------
# Step 4: Clean the project
# ----------------------------
Write-Host "`n=== Cleaning MultilayerCacheMetricsDemo ==="
dotnet clean $sampleProjectFile
if ($LASTEXITCODE -ne 0) {
    Write-Error "❌ dotnet clean failed."
    exit $LASTEXITCODE
} else {
    Write-Host "✅ Clean successful."
}

# ----------------------------
# Step 5: Build the project
# ----------------------------
Write-Host "`n=== MultilayerCacheMetricsDemo ==="
dotnet build $sampleProjectFile -c $Configuration
if ($LASTEXITCODE -ne 0) {
    Write-Error "❌ dotnet build failed."
    exit $LASTEXITCODE
} else {
    Write-Host "✅ Build successful."
}

# ----------------------------
# Step 6: Run the project
# ----------------------------
Write-Host "`n=== Running MultilayerCacheDemo ==="
Write-Host "▶ Starting via dotnet run..."
Start-Process "dotnet" -ArgumentList "run --project `"$sampleProjectFile`" -c $Configuration" -NoNewWindow
Write-Host "✅ MultilayerCache Metrics Demo started."