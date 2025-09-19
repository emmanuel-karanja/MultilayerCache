# Path to the project output folder
$ProjectFolderName = "MultilayerCache.Metrics.Demo"  # Change to "MultilayerCache.Demo" for the other demo
$BinFolder = Join-Path $PSScriptRoot "$ProjectFolderName\bin\Release\net8.0"

# Find the .exe that matches the folder/project name
$Exe = Get-ChildItem -Path $BinFolder -Filter "$ProjectFolderName.exe" | Select-Object -First 1

if (-not $Exe) {
    Write-Error "No executable found for $ProjectFolderName in $BinFolder. Make sure the project is built."
    exit 1
}

# --- Start Redis via Docker Compose ---
Write-Host "Ensuring Redis is running via Docker Compose..."
docker-compose -f "$PSScriptRoot\docker-compose.yml" up -d redis

# --- Wait for Redis to be ready ---
$maxAttempts = 10
$attempt = 0
$ready = $false
while (-not $ready -and $attempt -lt $maxAttempts) {
    try {
        $pong = docker exec redis redis-cli ping
        if ($pong -eq "PONG") {
            $ready = $true
            Write-Host "Redis is ready."
        }
    } catch {
        Write-Host "Waiting for Redis..."
        Start-Sleep -Seconds 2
    }
    $attempt++
}

if (-not $ready) {
    Write-Error "Redis did not start in time. Exiting."
    exit 1
}

# --- Run the demo executable ---
Write-Host "Running $ProjectFolderName..."
Start-Process -NoNewWindow -FilePath $Exe.FullName
