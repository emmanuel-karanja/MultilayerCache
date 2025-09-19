# Use official .NET SDK for build
FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /app

# Copy solution and projects
COPY ../MultilayerCache.sln .
COPY ../MultilayerCache/MultilayerCache.csproj ./MultilayerCache/
COPY ./MultilayerCache.Demo/MultilayerCache.Demo.csproj ./MultilayerCache.Demo/
COPY ./MultilayerCache.Metrics/MultilayerCache.Metrics.csproj ./MultilayerCache.Metrics/
COPY ./MultilayerCache.Metrics.Demo/MultilayerCache.Metrics.Demo.csproj ./MultilayerCache.Metrics.Demo/
COPY ../MultilayerCache.Tests/MultilayerCache.Tests.csproj ./MultilayerCache.Tests/

# Restore dependencies
RUN dotnet restore

# Copy everything else
COPY ../MultilayerCache ./MultilayerCache
COPY ./MultilayerCache.Demo ./MultilayerCache.Demo
COPY ./MultilayerCache.Metrics ./MultilayerCache.Metrics
COPY ./MultilayerCache.Metrics.Demo ./MultilayerCache.Metrics.Demo
COPY ../MultilayerCache.Tests ./MultilayerCache.Tests

# Build demo project
WORKDIR /app/MultilayerCache.Metrics.Demo
RUN dotnet publish -c Release -o out

# Final runtime image
FROM mcr.microsoft.com/dotnet/runtime
