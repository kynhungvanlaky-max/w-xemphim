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

# Install PostgreSQL, locales, curl, ca-certificates, and procps
ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update && apt-get install -y --no-install-recommends \
    postgresql \
    postgresql-contrib \
    locales \
    curl \
    ca-certificates \
    procps \
    && rm -rf /var/lib/apt/lists/*

# Generate UTF-8 locale
RUN echo "en_US.UTF-8 UTF-8" > /etc/locale.gen && locale-gen
ENV LANG=en_US.UTF-8 \
    LANGUAGE=en_US:en \
    LC_ALL=en_US.UTF-8

# Configure PostgreSQL trust authentication and pre-create database & user
RUN sed -i 's/scram-sha-256/trust/g' /etc/postgresql/*/main/pg_hba.conf && \
    sed -i 's/md5/trust/g' /etc/postgresql/*/main/pg_hba.conf && \
    mkdir -p /var/run/postgresql /var/log/postgresql && \
    chown -R postgres:postgres /var/run/postgresql /var/log/postgresql /var/lib/postgresql && \
    chmod 0775 /var/run/postgresql && \
    PG_DIR=$(ls -d /usr/lib/postgresql/* | sort -V | tail -n 1) && \
    DATA_DIR=$(ls -d /var/lib/postgresql/*/main | sort -V | tail -n 1) && \
    su - postgres -c "$PG_DIR/bin/pg_ctl -D $DATA_DIR -l /var/log/postgresql/init.log start" && \
    su - postgres -c "psql -c 'CREATE DATABASE webxemphim;'" && \
    su - postgres -c "psql -c \"CREATE USER webxemphim WITH PASSWORD 'webxemphim';\"" && \
    su - postgres -c "psql -c 'GRANT ALL PRIVILEGES ON DATABASE webxemphim TO webxemphim;'" && \
    su - postgres -c "psql -c 'ALTER USER webxemphim CREATEDB;'" && \
    su - postgres -c "psql -d webxemphim -c 'GRANT ALL ON SCHEMA public TO webxemphim;'" && \
    su - postgres -c "$PG_DIR/bin/pg_ctl -D $DATA_DIR stop"

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
