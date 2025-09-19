<#
.SYNOPSIS
Automates dependency setup, .proto compilation, build, and starting Redis for the MultilayerCache solution.
#>

param(
    [switch]$Clean
)

$SolutionRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProtosFolder = Join-Path $SolutionRoot "MultilayerCache.Demo\Protos"
$GeneratedFolder = Join-Path $SolutionRoot "MultilayerCache.Demo\Generated"

# --- Ensure protoc exists ---
function Ensure-Protoc {
    Write-Host "Checking for protoc compiler..."
    $protocCmd = "protoc"

    if (Get-Command $protocCmd -ErrorAction SilentlyContinue) {
        Write-Host "protoc found in PATH."
        return
    }

    $localProtocDir = Join-Path $env:USERPROFILE "protoc"
    $binDir = Join-Path $localProtocDir "bin"
    if (Test-Path $binDir -PathType Container) {
        $env:PATH += ";$binDir"
        if (Get-Command $protocCmd -ErrorAction SilentlyContinue) {
            Write-Host "protoc detected from local folder: $binDir"
            return
        }
    }

    $url = "https://github.com/protocolbuffers/protobuf/releases/download/v23.5/protoc-23.5-win64.zip"
    $zipPath = Join-Path $env:TEMP "protoc.zip"

    try {
        Write-Host "Downloading protoc..."
        Invoke-WebRequest -Uri $url -OutFile $zipPath -UseBasicParsing
    } catch {
        Write-Warning "Failed to download protoc from $url. Please download manually."
        Write-Error "protoc not installed. Aborting."
        exit 1
    }

    Expand-Archive -Path $zipPath -DestinationPath $localProtocDir -Force
    Remove-Item $zipPath -Force
    $env:PATH += ";$binDir"

    if (Get-Command $protocCmd -ErrorAction SilentlyContinue) {
        Write-Host "protoc installed locally to $binDir"
    } else {
        Write-Error "protoc still not found. Please install manually."
        exit 1
    }
}

# --- Clean bin/obj folders ---
function Clean-Projects {
    Write-Host "Cleaning bin and obj folders..."
    Get-ChildItem -Path $SolutionRoot -Recurse -Include bin,obj | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}

# --- Compile .proto files ---
function Compile-Protos {
    Ensure-Protoc
    Write-Host "Compiling .proto files..."

    if (-not (Test-Path $ProtosFolder)) { New-Item -ItemType Directory -Path $ProtosFolder -Force }
    if (-not (Test-Path $GeneratedFolder)) { New-Item -ItemType Directory -Path $GeneratedFolder -Force }

    $protoFiles = Get-ChildItem -Path $ProtosFolder -Filter *.proto
    if ($protoFiles.Count -eq 0) {
        Write-Warning "No .proto files found in $ProtosFolder"
        return
    }

    foreach ($file in $protoFiles) {
        Write-Host "Compiling $($file.Name)..."
        & protoc --csharp_out=$GeneratedFolder --proto_path=$ProtosFolder $file.FullName
    }
}

# --- Restore NuGet packages ---
function Restore-Packages {
    Write-Host "Restoring NuGet packages..."
    dotnet restore $SolutionRoot\MultilayerCache.sln
}

# --- Build all projects ---
function Build-Projects {
    Write-Host "Building all projects..."
    dotnet build $SolutionRoot\MultilayerCache.sln -c Release
}

# --- Start Redis via Docker Compose ---
function Start-Redis {
    Write-Host "Starting Redis container via Docker Compose..."
    docker-compose -f "$SolutionRoot\docker-compose.yml" up -d redis

    # Wait for Redis to be ready
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
}

# --- MAIN ---
Ensure-Protoc

if ($Clean) { Clean-Projects }

Compile-Protos
Restore-Packages
Build-Projects
Start-Redis

Write-Host "Build completed and Redis is running."
