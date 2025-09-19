<#
.SYNOPSIS
Automates dependency setup, .proto compilation, build, test, and running the Metrics Demo for MultilayerCache solution
#>

param(
    [switch]$Clean,
    [switch]$BuildOnly,
    [switch]$RunOnly,
    [switch]$RunMetricsDemo
)

$SolutionRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProtosFolder = Join-Path $SolutionRoot "MultilayerCache.Demo\Protos"
$GeneratedFolder = Join-Path $SolutionRoot "MultilayerCache.Demo\Generated"
$MetricsDemoFolder = Join-Path $SolutionRoot "MultilayerCache.Metrics.Demo"

# Function: Ensure protoc exists
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

# Function: Clean bin/obj folders
function Clean-Projects {
    Write-Host "Cleaning bin and obj folders..."
    Get-ChildItem -Path $SolutionRoot -Recurse -Include bin,obj | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}

# Function: Compile .proto files
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

# Function: Restore NuGet packages
function Restore-Packages {
    Write-Host "Restoring NuGet packages..."
    dotnet restore $SolutionRoot\MultilayerCache.sln
}

# Function: Build all projects
function Build-Projects {
    Write-Host "Building all projects..."
    dotnet build $SolutionRoot\MultilayerCache.sln -c Release
}

# Function: Run unit tests
function Run-Tests {
    Write-Host "Running unit tests..."
    dotnet test $SolutionRoot\MultilayerCache.Tests\MultilayerCache.Tests.csproj -c Release
}

# Function: Run Docker Compose
function Run-Docker {
    Write-Host "Building and starting Docker containers..."
    docker-compose -f "$SolutionRoot\docker-compose.yml" up --build
}

# Function: Run Metrics Demo
function Run-MetricsDemo {
    Write-Host "Running MultiLayerCache.Metrics.Demo..."
    dotnet run --project $MetricsDemoFolder -c Release
}

# MAIN

Ensure-Protoc

if ($Clean) { Clean-Projects }

Compile-Protos

if (-not $RunOnly) {
    Restore-Packages
    Build-Projects
    Run-Tests
}

if ($RunMetricsDemo) {
    Run-MetricsDemo
} elseif (-not $BuildOnly) {
    Run-Docker
}

Write-Host "All tasks completed."
