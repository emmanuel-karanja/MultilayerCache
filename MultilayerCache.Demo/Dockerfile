# Use official .NET SDK for build
FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /app

# Copy solution and project files
COPY MultilayerCache.sln . 
COPY MultilayerCache/MultilayerCache.csproj ./MultilayerCache/
COPY MultilayerCache.Metrics/MultilayerCache.Metrics.csproj ./MultilayerCache.Metrics/
COPY MultilayerCache.Demo/MultilayerCache.Demo.csproj ./MultilayerCache.Demo/
COPY MultilayerCache.Tests/MultilayerCache.Tests.csproj ./MultilayerCache.Tests/

# Restore dependencies
RUN dotnet restore

# Copy all source files
COPY MultilayerCache ./MultilayerCache
COPY MultilayerCache.Metrics ./MultilayerCache.Metrics
COPY MultilayerCache.Demo ./MultilayerCache.Demo
COPY MultilayerCache.Tests ./MultilayerCache.Tests

# Build demo project (works for both Demo projects)
WORKDIR /app/MultilayerCache.Demo
RUN dotnet publish -c Release -o out

# Final runtime image
FROM mcr.microsoft.com/dotnet/runtime:7.0
WORKDIR /app
COPY --from=build /app/MultilayerCache.Demo/out ./out

ENTRYPOINT ["dotnet", "out/MultilayerCache.Demo.dll"]
