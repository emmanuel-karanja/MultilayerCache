# Path to the metrics demo project
$MetricsDemoFolder = Join-Path $PSScriptRoot "MultilayerCache.Metrics.Demo"

Write-Host "Running MultiLayerCache.Metrics.Demo..."
dotnet run --project $MetricsDemoFolder -c Release
