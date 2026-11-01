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

# Initialize PostgreSQL cluster during build so it never fails at runtime
RUN mkdir -p /var/lib/postgresql/data /var/run/postgresql /var/log/postgresql && \
    chown -R postgres:postgres /var/lib/postgresql /var/run/postgresql /var/log/postgresql && \
    chmod 0700 /var/lib/postgresql/data && \
    chmod 0775 /var/run/postgresql && \
    PG_BIN=$(dirname $(find /usr/lib/postgresql/ -name postgres | sort -V | tail -n 1)) && \
    su - postgres -c "$PG_BIN/initdb -D /var/lib/postgresql/data --locale=en_US.UTF-8 --encoding=UTF8 --auth-local=trust --auth-host=trust" && \
    su - postgres -c "$PG_BIN/pg_ctl -D /var/lib/postgresql/data -l /var/log/postgresql/init.log start" && \
    su - postgres -c "$PG_BIN/psql -c 'CREATE DATABASE webxemphim;'" && \
    su - postgres -c "$PG_BIN/psql -c \"CREATE USER webxemphim WITH PASSWORD 'webxemphim';\"" && \
    su - postgres -c "$PG_BIN/psql -c 'GRANT ALL PRIVILEGES ON DATABASE webxemphim TO webxemphim;'" && \
    su - postgres -c "$PG_BIN/psql -c 'ALTER USER webxemphim CREATEDB;'" && \
    su - postgres -c "$PG_BIN/psql -d webxemphim -c 'GRANT ALL ON SCHEMA public TO webxemphim;'" && \
    su - postgres -c "$PG_BIN/pg_ctl -D /var/lib/postgresql/data stop"

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
