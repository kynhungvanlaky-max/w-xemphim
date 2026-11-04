#!/bin/bash
set -e

echo "=========================================================="
echo "CinemaFlow Zero-Config All-in-One Runner (Render)"
echo "=========================================================="

PG_DIR=$(ls -d /usr/lib/postgresql/* 2>/dev/null | sort -V | tail -n 1)
DATA_DIR=$(ls -d /var/lib/postgresql/*/main 2>/dev/null | sort -V | tail -n 1)

cleanup() {
    echo "Shutting down CinemaFlow services..."
    kill -TERM "$CLIENT_PID" 2>/dev/null || true
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
    wait "$CLIENT_PID" 2>/dev/null || true
    wait "$SERVER_PID" 2>/dev/null || true
    exit 0
}

trap cleanup SIGTERM SIGINT

export PORT=${PORT:-10000}
export ASPNETCORE_ENVIRONMENT=${ASPNETCORE_ENVIRONMENT:-Production}
export DOTNET_ENVIRONMENT=${DOTNET_ENVIRONMENT:-Production}
export DOTNET_RUNNING_IN_CONTAINER=true
export Security__DataProtectionKeyPath=${Security__DataProtectionKeyPath:-/app/keys}
export Security__BehindReverseProxy=${Security__BehindReverseProxy:-true}
export ApiBaseUrl="http://127.0.0.1:5085"

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
    if [ -z "$Security__PublicApiBaseUrl" ] && [ -n "$RENDER_EXTERNAL_HOSTNAME" ]; then
        export Security__PublicApiBaseUrl="https://${RENDER_EXTERNAL_HOSTNAME}"
    fi
fi

# Determine if we should use local in-container PostgreSQL or external database
USE_LOCAL_PG="false"
if [ -z "$DATABASE_URL" ] && [ -z "$ConnectionStrings__Default" ]; then
    USE_LOCAL_PG="true"
fi

if [ "$USE_LOCAL_PG" = "true" ]; then
    echo "=== [1/3] Starting In-Container PostgreSQL Engine ==="
    mkdir -p /var/run/postgresql /var/log/postgresql
    chown -R postgres:postgres /var/run/postgresql /var/log/postgresql /var/lib/postgresql
    chmod 0775 /var/run/postgresql
    chmod 0700 /var/lib/postgresql/*/main 2>/dev/null || true

    # Configure trust authentication
    sed -i 's/peer/trust/g' /etc/postgresql/*/main/pg_hba.conf 2>/dev/null || true
    sed -i 's/scram-sha-256/trust/g' /etc/postgresql/*/main/pg_hba.conf 2>/dev/null || true
    sed -i 's/md5/trust/g' /etc/postgresql/*/main/pg_hba.conf 2>/dev/null || true

    # Start PostgreSQL service
    echo "Starting PostgreSQL daemon..."
    if [ -x /etc/init.d/postgresql ]; then
        /etc/init.d/postgresql start || true
    elif command -v service >/dev/null 2>&1; then
        service postgresql start || true
    elif [ -n "$PG_DIR" ] && [ -n "$DATA_DIR" ]; then
        su - postgres -c "$PG_DIR/bin/pg_ctl -D $DATA_DIR -l /var/log/postgresql/pg.log -o \"-c listen_addresses='*'\" start" || true
    fi

    # Wait for PostgreSQL to become ready
    PG_READY=false
    for i in $(seq 1 30); do
        if su - postgres -c "pg_isready -h 127.0.0.1 -p 5432" >/dev/null 2>&1 || pg_isready >/dev/null 2>&1; then
            echo "PostgreSQL is ready on port 5432!"
            PG_READY=true
            break
        fi
        sleep 1
    done

    if [ "$PG_READY" != "true" ]; then
        echo "WARNING: PostgreSQL check did not return immediately, checking cluster logs:"
        cat /var/log/postgresql/*.log 2>/dev/null || true
    fi

    # Ensure database and user exist
    echo "Provisioning local database 'webxemphim' and user 'webxemphim'..."
    su - postgres -c "psql -tc \"SELECT 1 FROM pg_roles WHERE rolname = 'webxemphim'\" | grep -q 1 || psql -c \"CREATE USER webxemphim WITH PASSWORD 'webxemphim' CREATEDB;\"" || true
    su - postgres -c "psql -tc \"SELECT 1 FROM pg_database WHERE datname = 'webxemphim'\" | grep -q 1 || psql -c \"CREATE DATABASE webxemphim OWNER webxemphim;\"" || true
    su - postgres -c "psql -c \"GRANT ALL PRIVILEGES ON DATABASE webxemphim TO webxemphim;\"" || true
    su - postgres -c "psql -d webxemphim -c \"GRANT ALL ON SCHEMA public TO webxemphim;\"" || true

    export ConnectionStrings__Default="Host=127.0.0.1;Port=5432;Database=webxemphim;Username=webxemphim;Password=webxemphim;SSL Mode=Prefer"
    export DATABASE_URL="$ConnectionStrings__Default"
    echo "Local database initialized successfully."
else
    echo "Using external PostgreSQL database provided via environment."
    if [ -n "$DATABASE_URL" ] && [ -z "$ConnectionStrings__Default" ]; then
        export ConnectionStrings__Default="$DATABASE_URL"
    fi
fi

echo "=== [2/3] Launching CinemaFlow REST API Server (127.0.0.1:5085) ==="
cd /app/server
dotnet WebXemPhim.Server.dll --urls "http://127.0.0.1:5085" &
SERVER_PID=$!
echo "Server PID: $SERVER_PID"

# Wait for Server to complete EF migrations and become healthy
echo "Waiting for Server to be ready on 127.0.0.1:5085..."
SERVER_HEALTHY=false
for i in $(seq 1 45); do
    if curl -sf http://127.0.0.1:5085/api/health >/dev/null 2>&1 || curl -sf http://127.0.0.1:5085/health >/dev/null 2>&1; then
        echo "CinemaFlow Server is healthy on 127.0.0.1:5085!"
        SERVER_HEALTHY=true
        break
    fi

    if ! kill -0 "$SERVER_PID" 2>/dev/null; then
        echo "CinemaFlow Server died during startup!"
        exit 1
    fi
    sleep 1
done

if [ "$SERVER_HEALTHY" != "true" ]; then
    echo "CinemaFlow Server timed out waiting for /api/health!"
    exit 1
fi

echo "=== [3/3] Launching CinemaFlow Web Client (0.0.0.0:$PORT) ==="
cd /app/client
dotnet WebXemPhim.Client.dll --urls "http://0.0.0.0:${PORT}" &
CLIENT_PID=$!
echo "Client PID: $CLIENT_PID"

echo "=========================================================="
echo "CinemaFlow is now serving on port $PORT"
echo "=========================================================="

wait -n "$SERVER_PID" "$CLIENT_PID"
EXIT_CODE=$?
echo "A CinemaFlow process exited with code $EXIT_CODE."
cleanup
