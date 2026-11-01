# Multi-stage Dockerfile for CinemaFlow All-in-One Deployment on Render (.NET 10)
# Build Context: Root directory of repository

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Step 1: Copy csproj files for layer caching
COPY ["src/Shared/WebXemPhim.Shared/WebXemPhim.Shared.csproj", "src/Shared/WebXemPhim.Shared/"]
COPY ["src/Server/WebXemPhim.Server/WebXemPhim.Server.csproj", "src/Server/WebXemPhim.Server/"]
COPY ["src/Client/WebXemPhim.Client/WebXemPhim.Client.csproj", "src/Client/WebXemPhim.Client/"]

# Restore dependencies
RUN dotnet restore "src/Server/WebXemPhim.Server/WebXemPhim.Server.csproj" && \
    dotnet restore "src/Client/WebXemPhim.Client/WebXemPhim.Client.csproj"

# Step 2: Copy full source and publish Server and Client in Release mode
COPY src/Shared/ src/Shared/
COPY src/Server/WebXemPhim.Server/ src/Server/WebXemPhim.Server/
COPY src/Client/WebXemPhim.Client/ src/Client/WebXemPhim.Client/

WORKDIR "/src/src/Server/WebXemPhim.Server"
RUN dotnet publish "WebXemPhim.Server.csproj" -c Release -o /app/server /p:UseAppHost=false

WORKDIR "/src/src/Client/WebXemPhim.Client"
RUN dotnet publish "WebXemPhim.Client.csproj" -c Release -o /app/client /p:UseAppHost=false

# Step 3: Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Install PostgreSQL, curl, ca-certificates, and procps
ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update && apt-get install -y --no-install-recommends \
    postgresql \
    postgresql-contrib \
    curl \
    ca-certificates \
    procps \
    && rm -rf /var/lib/apt/lists/*

# Setup application directories and persistent storage
RUN mkdir -p /app/server /app/client /app/keys /app/data

# Copy published binaries
COPY --from=build /app/server /app/server
COPY --from=build /app/client /app/client

# Copy and prepare entrypoint script
COPY docker-entrypoint.sh /app/docker-entrypoint.sh
RUN chmod +x /app/docker-entrypoint.sh

# Environment variables
ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    Security__DataProtectionKeyPath=/app/keys \
    Security__BehindReverseProxy=true \
    AllowedHosts=* \
    PORT=10000

EXPOSE 10000 8080

ENTRYPOINT ["/app/docker-entrypoint.sh"]
