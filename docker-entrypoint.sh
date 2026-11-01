#!/bin/bash
set -e

echo "=========================================================="
echo "CinemaFlow All-in-One Runner (Render Deployment)"
echo "=========================================================="

cleanup() {
    echo "Shutting down CinemaFlow services..."
    kill -TERM "$SERVER_PID" 2>/dev/null || true
    kill -TERM "$CLIENT_PID" 2>/dev/null || true
    wait "$SERVER_PID" 2>/dev/null || true
    wait "$CLIENT_PID" 2>/dev/null || true
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

# Local loopback bridge between Client and Server
export ApiBaseUrl="http://127.0.0.1:5085"

# If DATABASE_URL is provided by Render postgres, ensure ConnectionStrings__Default is populated
if [ -n "$DATABASE_URL" ] && [ -z "$ConnectionStrings__Default" ]; then
    export ConnectionStrings__Default="$DATABASE_URL"
fi

echo "=== [1/2] Launching CinemaFlow REST API Server (127.0.0.1:5085) ==="
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

echo "=== [2/2] Launching CinemaFlow Web Client (0.0.0.0:$PORT) ==="
cd /app/client
dotnet WebXemPhim.Client.dll --urls "http://0.0.0.0:${PORT}" &
CLIENT_PID=$!
echo "Client PID: $CLIENT_PID"

echo "=========================================================="
echo "CinemaFlow is now serving on port $PORT"
echo "=========================================================="

# Wait for either process to exit; if one exits, shut down both so Docker container restarts
wait -n "$SERVER_PID" "$CLIENT_PID"
EXIT_CODE=$?
echo "A CinemaFlow process exited with code $EXIT_CODE. Exiting container..."
cleanup
