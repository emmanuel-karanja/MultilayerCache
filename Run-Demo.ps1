# Path to the demo project
$DemoFolder = Join-Path $PSScriptRoot "MultilayerCache.Demo"

Write-Host "Running MultiLayerCache.Demo..."
dotnet run --project $DemoFolder -c Release
