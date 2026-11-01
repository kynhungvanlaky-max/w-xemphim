#!/bin/bash
set -e

echo "=========================================================="
echo "CinemaFlow Zero-Config All-in-One Runner (Render)"
echo "=========================================================="

cleanup() {
    echo "Shutting down CinemaFlow services..."
    kill -TERM "$CLIENT_PID" 2>/dev/null || true
    kill -TERM "$SERVER_PID" 2>/dev/null || true
    if [ "$USE_LOCAL_PG" = "true" ]; then
        /etc/init.d/postgresql stop 2>/dev/null || true
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
export Security__BehindReverseProxy=true
export AllowedHosts="*"
export ApiBaseUrl="http://127.0.0.1:5085"

# Fallback default HMAC signing keys if not passed by environment
if [ -z "$Security__StreamSigningKey" ] || [ "${#Security__StreamSigningKey}" -lt 32 ]; then
    export Security__StreamSigningKey="CinemaFlow_Production_Stream_Signing_HMAC_Secret_2026_Key!"
fi
if [ -z "$Payment__WebhookSecret" ] || [ "${#Payment__WebhookSecret}" -lt 32 ]; then
    export Payment__WebhookSecret="CinemaFlow_Production_Payment_Webhook_HMAC_SHA512_Secret_2026_Key!"
fi

# Determine if we should use local in-container PostgreSQL or external database
USE_LOCAL_PG="false"
if [ -z "$DATABASE_URL" ] && [ -z "$ConnectionStrings__Default" ]; then
    USE_LOCAL_PG="true"
fi

if [ "$USE_LOCAL_PG" = "true" ]; then
    echo "=== [1/3] Starting In-Container PostgreSQL Engine ==="
    # Configure trust authentication for loopback connections
    for hba in /etc/postgresql/*/main/pg_hba.conf; do
        if [ -f "$hba" ]; then
            sed -i 's/scram-sha-256/trust/g' "$hba" 2>/dev/null || true
            sed -i 's/md5/trust/g' "$hba" 2>/dev/null || true
        fi
    done

    /etc/init.d/postgresql start
    
    # Wait for PostgreSQL to become ready
    for i in $(seq 1 30); do
        if su - postgres -c "pg_isready" >/dev/null 2>&1; then
            echo "PostgreSQL is ready!"
            break
        fi
        sleep 1
    done

    # Setup database and user
    su - postgres -c "psql -tc \"SELECT 1 FROM pg_database WHERE datname = 'webxemphim'\" | grep -q 1 || psql -c \"CREATE DATABASE webxemphim;\""
    su - postgres -c "psql -tc \"SELECT 1 FROM pg_roles WHERE rolname = 'webxemphim'\" | grep -q 1 || psql -c \"CREATE USER webxemphim WITH PASSWORD 'webxemphim';\""
    su - postgres -c "psql -c \"GRANT ALL PRIVILEGES ON DATABASE webxemphim TO webxemphim;\""
    su - postgres -c "psql -c \"ALTER USER webxemphim CREATEDB;\""
    su - postgres -c "psql -d webxemphim -c \"GRANT ALL ON SCHEMA public TO webxemphim;\""

    export ConnectionStrings__Default="Host=127.0.0.1;Port=5432;Database=webxemphim;Username=webxemphim;Password=webxemphim;SSL Mode=Prefer"
    export DATABASE_URL="$ConnectionStrings__Default"
    echo "Configured local database connection."
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
echo "Waiting for Server to be ready..."
for i in $(seq 1 45); do
    if curl -s http://127.0.0.1:5085/api/health >/dev/null 2>&1; then
        echo "Server is healthy on 127.0.0.1:5085!"
        break
    fi
    sleep 1
done

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
echo "A CinemaFlow process exited with code $EXIT_CODE. Exiting container..."
cleanup
