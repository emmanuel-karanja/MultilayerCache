<#
.SYNOPSIS
    Cleans, builds, ensures Redis is running on a free port, stops previous instances, and runs MultilayerCache.Demo.
.DESCRIPTION
    Restores dependencies, cleans and builds the MultilayerCache.Demo, starts Redis via docker-compose
    on an available port, waits for Redis to be ready, stops previous running instances, and runs the microservice.
#>

param (
    [string]$SolutionRoot = ".",
    [string]$Configuration = "Debug",        # Use Release if needed
    [string]$Runtime = "win-x64",            # Change to linux-x64 if on Linux
    [string]$DockerComposeFile = ".\docker-compose.yml",
    [int]$MaxRedisRetries = 10,
    [int]$RedisRetryDelaySeconds = 3
)

# --- Paths ---
$sampleProjectDir  = Join-Path $SolutionRoot "MultilayerCache.Demo"
$sampleProjectFile = Join-Path $sampleProjectDir "MultilayerCache.Demo.csproj"

# --- Prerequisites ---
Write-Host "`n=== Checking prerequisites ==="
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "❌ .NET SDK not found. Please install .NET SDK to continue."
    exit 1
} else { Write-Host "✅ .NET SDK found." }

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Write-Error "❌ Docker not found. Please install Docker to continue."
    exit 1
} else { Write-Host "✅ Docker CLI found." }

# --- Stop previous instances ---
Write-Host "`n=== Stopping previously running MultilayerCacheDemo instances ==="
$existingProcesses = Get-Process -Name "dotnet" -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path -match "MultilayerCache.Demo.dll" }
if ($existingProcesses) {
    foreach ($proc in $existingProcesses) {
        Write-Host "Stopping process Id $($proc.Id)..."
        Stop-Process -Id $proc.Id -Force
    }
    Write-Host "✅ Previous instances stopped."
} else {
    Write-Host "No existing instances found."
}



$redisPort = 6379

Write-Host "✅ Selected Redis port: $redisPort"

# --- Start Redis ---
Write-Host "`n=== Starting Redis via docker-compose on port $redisPort ==="
if (-Not (Test-Path $DockerComposeFile)) {
    Write-Error "❌ docker-compose.yml not found at $DockerComposeFile"
    exit 1
}

$env:REDIS_PORT = $redisPort
docker-compose -f $DockerComposeFile up -d --remove-orphans
if ($LASTEXITCODE -ne 0) {
    Write-Error "❌ Failed to start Redis via docker-compose."
    exit $LASTEXITCODE
} else { Write-Host "✅ Redis container started (detached) on $redisPort." }

# --- Wait for Redis ---
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
} else { Write-Host "✅ Redis is healthy and ready!" }

# --- Clean and Build ---
Write-Host "`n=== Cleaning MultilayerCacheDemo ==="
dotnet clean $sampleProjectFile
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "`n=== Building MultilayerCacheDemo ==="
dotnet build $sampleProjectFile -c $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# --- Run the project ---
Write-Host "`n=== Running MultilayerCacheDemo ==="
Start-Process "dotnet" -ArgumentList "run --project `"$sampleProjectFile`" -c $Configuration" -NoNewWindow
Write-Host "✅ MultilayerCache Demo started (connected to Redis on port $redisPort)."
