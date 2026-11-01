#!/bin/bash
set -e

echo "=========================================================="
echo "CinemaFlow Zero-Config All-in-One Runner (Render)"
echo "=========================================================="

cleanup() {
    echo "Shutting down CinemaFlow services..."
    kill -TERM "$CLIENT_PID" 2>/dev/null || true
    kill -TERM "$SERVER_PID" 2>/dev/null || true
    if [ "$USE_LOCAL_PG" = "true" ] && [ -n "$PG_BIN" ]; then
        su - postgres -c "$PG_BIN/pg_ctl -D /var/lib/postgresql/data stop" 2>/dev/null || true
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
    PG_BIN=$(dirname $(find /usr/lib/postgresql/ -name postgres 2>/dev/null | sort -V | tail -n 1))
    PG_DATA="/var/lib/postgresql/data"

    mkdir -p /var/run/postgresql /var/log/postgresql
    chown -R postgres:postgres /var/run/postgresql /var/log/postgresql /var/lib/postgresql
    chmod 0775 /var/run/postgresql

    # Start PostgreSQL
    su - postgres -c "$PG_BIN/pg_ctl -D $PG_DATA -l /var/log/postgresql/pg.log start" || true

    # Wait for PostgreSQL to become ready
    PG_READY=false
    for i in $(seq 1 20); do
        if su - postgres -c "$PG_BIN/pg_isready -h 127.0.0.1 -p 5432" >/dev/null 2>&1; then
            echo "PostgreSQL is ready on 127.0.0.1:5432!"
            PG_READY=true
            break
        fi
        sleep 1
    done

    if [ "$PG_READY" != "true" ]; then
        echo "PostgreSQL failed to start on 127.0.0.1:5432! Log output:"
        cat /var/log/postgresql/pg.log 2>/dev/null || true
    fi

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
dotnet WebXemPhim.Server.dll --urls "http://127.0.0.1:5085" > /var/log/server.log 2>&1 &
SERVER_PID=$!
echo "Server PID: $SERVER_PID"

# Wait for Server to complete EF migrations and become healthy
echo "Waiting for Server to be ready on 127.0.0.1:5085..."
SERVER_HEALTHY=false
for i in $(seq 1 45); do
    if curl -s http://127.0.0.1:5085/api/health >/dev/null 2>&1; then
        echo "CinemaFlow Server is healthy on 127.0.0.1:5085!"
        SERVER_HEALTHY=true
        break
    fi

    if ! kill -0 "$SERVER_PID" 2>/dev/null; then
        echo "CinemaFlow Server died during startup! Showing /var/log/server.log:"
        cat /var/log/server.log 2>/dev/null || true
        exit 1
    fi
    sleep 1
done

if [ "$SERVER_HEALTHY" != "true" ]; then
    echo "CinemaFlow Server timed out waiting for /api/health! Showing /var/log/server.log:"
    cat /var/log/server.log 2>/dev/null || true
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
cat /var/log/server.log 2>/dev/null || true
cleanup
