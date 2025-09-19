<#
.SYNOPSIS
Automates dependency setup, .proto compilation, build, test, and Docker run for MultilayerCache solution
#>

param(
    [switch]$Clean,
    [switch]$BuildOnly,
    [switch]$RunOnly
)

$SolutionRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProtosFolder = Join-Path $SolutionRoot "MultilayerCache.Demo\Protos"
$GeneratedFolder = Join-Path $SolutionRoot "MultilayerCache.Demo\Generated"

# Function: Ensure protoc exists
function Ensure-Protoc {
    Write-Host "Checking for protoc compiler..."
    $protocCmd = "protoc"

    # 1️⃣ Already in PATH
    if (Get-Command $protocCmd -ErrorAction SilentlyContinue) {
        Write-Host "protoc found in PATH."
        return
    }

    # 2️⃣ Check local folder
    $localProtocDir = Join-Path $env:USERPROFILE "protoc"
    $binDir = Join-Path $localProtocDir "bin"
    if (Test-Path $binDir -PathType Container) {
        $env:PATH += ";$binDir"
        if (Get-Command $protocCmd -ErrorAction SilentlyContinue) {
            Write-Host "protoc detected from local folder: $binDir"
            return
        }
    }

    # 3️⃣ Attempt download (optional, ensure URL is valid)
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

    if (-not (Test-Path $zipPath)) {
        Write-Error "Downloaded protoc zip not found. Aborting."
        exit 1
    }

    # Extract
    Expand-Archive -Path $zipPath -DestinationPath $localProtocDir -Force
    Remove-Item $zipPath -Force

    # Update PATH
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

    # Create folders if missing
    if (-not (Test-Path $ProtosFolder)) {
        Write-Warning "Protos folder not found. Creating $ProtosFolder"
        New-Item -ItemType Directory -Path $ProtosFolder -Force
    }
    if (-not (Test-Path $GeneratedFolder)) {
        Write-Host "Generated folder not found. Creating $GeneratedFolder"
        New-Item -ItemType Directory -Path $GeneratedFolder -Force
    }

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

# Function: Build and run Docker Compose
function Run-Docker {
    Write-Host "Building and starting Docker containers..."
    docker-compose -f "$SolutionRoot\docker-compose.yml" up --build
}

# MAIN

# 1️⃣ Ensure protoc is available
Ensure-Protoc

# 2️⃣ Clean projects if requested
if ($Clean) {
    Clean-Projects
}

# 3️⃣ Compile .proto files
Compile-Protos

# 4️⃣ Restore packages, build, and test
if (-not $RunOnly) {
    Restore-Packages
    Build-Projects
    Run-Tests
}

# 5️⃣ Run Docker
if (-not $BuildOnly) {
    Run-Docker
}

Write-Host "All tasks completed."
