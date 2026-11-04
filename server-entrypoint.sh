#!/bin/bash
set -e

echo "=========================================================="
echo "CinemaFlow REST API Server Runner"
echo "=========================================================="

export PORT=${PORT:-10000}
export ASPNETCORE_ENVIRONMENT=${ASPNETCORE_ENVIRONMENT:-Production}
export DOTNET_ENVIRONMENT=${DOTNET_ENVIRONMENT:-Production}
export DOTNET_RUNNING_IN_CONTAINER=true
export Security__DataProtectionKeyPath=${Security__DataProtectionKeyPath:-/app/keys}
export Security__BehindReverseProxy=${Security__BehindReverseProxy:-true}

# Never fall back to a public HMAC key or a wildcard host allowlist in Production.
# If not explicitly provided in environment variables, auto-derive safe values for Render/Cloud.
if [ "$ASPNETCORE_ENVIRONMENT" = "Production" ]; then
    if [ -z "$Security__StreamSigningKey" ] || [ "${#Security__StreamSigningKey}" -lt 32 ]; then
        export Security__StreamSigningKey=$(head -c 32 /dev/urandom | base64 | tr -dc 'a-zA-Z0-9' | head -c 48)
        echo "Notice: Auto-generated 48-char Security__StreamSigningKey for Production."
    fi
    if [ -z "$Payment__WebhookSecret" ] || [ "${#Payment__WebhookSecret}" -lt 32 ]; then
        export Payment__WebhookSecret=$(head -c 32 /dev/urandom | base64 | tr -dc 'a-zA-Z0-9' | head -c 48)
        echo "Notice: Auto-generated 48-char Payment__WebhookSecret for Production."
    fi
    if [ -z "$AllowedHosts" ] || [ "$AllowedHosts" = "*" ]; then
        if [ -n "$RENDER_EXTERNAL_HOSTNAME" ]; then
            export AllowedHosts="${RENDER_EXTERNAL_HOSTNAME};w-phim-api.onrender.com;localhost;127.0.0.1;[::1]"
        elif [ -n "$RENDER_SERVICE_NAME" ]; then
            export AllowedHosts="${RENDER_SERVICE_NAME}.onrender.com;w-phim-api.onrender.com;localhost;127.0.0.1;[::1]"
        else
            export AllowedHosts="w-phim-api.onrender.com;w-phim-client.onrender.com;localhost;127.0.0.1;[::1]"
        fi
        echo "Notice: AllowedHosts auto-configured as: $AllowedHosts"
    fi
    if [ -z "$Security__AllowedOrigins__0" ] && [ -z "$Security__AllowedOrigins" ]; then
        export Security__AllowedOrigins__0="https://w-phim-client.onrender.com"
    fi
    if [ -z "$Security__PublicApiBaseUrl" ]; then
        if [ -n "$RENDER_EXTERNAL_HOSTNAME" ]; then
            export Security__PublicApiBaseUrl="https://${RENDER_EXTERNAL_HOSTNAME}"
        else
            export Security__PublicApiBaseUrl="https://w-phim-api.onrender.com"
        fi
    fi
fi

# Determine if we should use local in-container PostgreSQL or external database
USE_LOCAL_PG="false"
if [ -z "$DATABASE_URL" ] && [ -z "$ConnectionStrings__Default" ]; then
    USE_LOCAL_PG="true"
fi

PG_DIR=$(ls -d /usr/lib/postgresql/* 2>/dev/null | sort -V | tail -n 1)
DATA_DIR=$(ls -d /var/lib/postgresql/*/main 2>/dev/null | sort -V | tail -n 1)

cleanup() {
    echo "Shutting down Server..."
    kill -TERM "$SERVER_PID" 2>/dev/null || true
    if [ "$USE_LOCAL_PG" = "true" ]; then
        if [ -x /etc/init.d/postgresql ]; then
            /etc/init.d/postgresql stop 2>/dev/null || true
        elif command -v service >/dev/null 2>&1; then
            service postgresql stop 2>/dev/null || true
        elif [ -n "$PG_DIR" ] && [ -n "$DATA_DIR" ]; then
            su - postgres -c "$PG_DIR/bin/pg_ctl -D $DATA_DIR stop" 2>/dev/null || true
        fi
    fi
    wait "$SERVER_PID" 2>/dev/null || true
    exit 0
}

trap cleanup SIGTERM SIGINT

if [ "$USE_LOCAL_PG" = "true" ]; then
    echo "=== [Notice] DATABASE_URL is not set: Starting In-Container PostgreSQL Engine ==="
    mkdir -p /var/run/postgresql /var/log/postgresql
    chown -R postgres:postgres /var/run/postgresql /var/log/postgresql /var/lib/postgresql 2>/dev/null || true
    chmod 0775 /var/run/postgresql 2>/dev/null || true
    chmod 0700 /var/lib/postgresql/*/main 2>/dev/null || true

    sed -i 's/peer/trust/g' /etc/postgresql/*/main/pg_hba.conf 2>/dev/null || true
    sed -i 's/scram-sha-256/trust/g' /etc/postgresql/*/main/pg_hba.conf 2>/dev/null || true
    sed -i 's/md5/trust/g' /etc/postgresql/*/main/pg_hba.conf 2>/dev/null || true

    if [ -x /etc/init.d/postgresql ]; then
        /etc/init.d/postgresql start || true
    elif command -v service >/dev/null 2>&1; then
        service postgresql start || true
    elif [ -n "$PG_DIR" ] && [ -n "$DATA_DIR" ]; then
        su - postgres -c "$PG_DIR/bin/pg_ctl -D $DATA_DIR -l /var/log/postgresql/pg.log -o \"-c listen_addresses='*'\" start" || true
    fi

    for i in $(seq 1 30); do
        if su - postgres -c "pg_isready -h 127.0.0.1 -p 5432" >/dev/null 2>&1 || pg_isready >/dev/null 2>&1; then
            echo "In-container PostgreSQL is ready on port 5432!"
            break
        fi
        sleep 1
    done

    su - postgres -c "psql -tc \"SELECT 1 FROM pg_roles WHERE rolname = 'webxemphim'\" | grep -q 1 || psql -c \"CREATE USER webxemphim WITH PASSWORD 'webxemphim' CREATEDB;\"" || true
    su - postgres -c "psql -tc \"SELECT 1 FROM pg_database WHERE datname = 'webxemphim'\" | grep -q 1 || psql -c \"CREATE DATABASE webxemphim OWNER webxemphim;\"" || true
    su - postgres -c "psql -c \"GRANT ALL PRIVILEGES ON DATABASE webxemphim TO webxemphim;\"" || true
    su - postgres -c "psql -d webxemphim -c \"GRANT ALL ON SCHEMA public TO webxemphim;\"" || true

    export ConnectionStrings__Default="Host=127.0.0.1;Port=5432;Database=webxemphim;Username=webxemphim;Password=webxemphim;SSL Mode=Prefer"
    export DATABASE_URL="$ConnectionStrings__Default"
else
    echo "=== Connecting to External Database provided via DATABASE_URL ==="
    if [ -n "$DATABASE_URL" ] && [ -z "$ConnectionStrings__Default" ]; then
        export ConnectionStrings__Default="$DATABASE_URL"
    fi
fi

echo "=== Launching CinemaFlow REST API Server (0.0.0.0:$PORT) ==="
dotnet WebXemPhim.Server.dll --urls "http://0.0.0.0:${PORT}" &
SERVER_PID=$!
echo "Server PID: $SERVER_PID"

wait "$SERVER_PID"
EXIT_CODE=$?
echo "Server exited with code $EXIT_CODE."
cleanup
