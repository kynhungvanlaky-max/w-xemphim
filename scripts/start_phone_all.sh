#!/bin/bash
set -e

echo "=== [1/5] Ensuring MariaDB is running ==="
/etc/init.d/mariadb start || true
for i in {1..15}; do
    if mariadb-admin ping >/dev/null 2>&1; then
        echo "MariaDB is UP"
        break
    fi
    sleep 1
done

echo "=== [2/5] Preparing .NET 10 Runtimes & Symlinks ==="
cd /opt/dotnet/shared/Microsoft.NETCore.App
ln -sfn 10.0.0-rc.2.25502.107 10.0.0 || true
cd /opt/dotnet/shared/Microsoft.AspNetCore.App
ln -sfn 10.0.0-rc.2.25502.107 10.0.0 || true

echo "=== [3/5] Stopping old instances ==="
killall -9 WebXemPhim.Server 2>/dev/null || true
killall -9 WebXemPhim.Client 2>/dev/null || true
killall -9 caddy 2>/dev/null || true
killall -9 cloudflared 2>/dev/null || true
pkill -9 -f WebXemPhim || true
sleep 1

export DOTNET_ROOT=/opt/dotnet
export PATH="$DOTNET_ROOT:$PATH"
export ASPNETCORE_ENVIRONMENT=Development
export DOTNET_ENVIRONMENT=Development
export AllowedHosts="*"
export Database__Provider=MySql
export Database__SeedDemoData=true
export ConnectionStrings__Default="Server=127.0.0.1;Port=3306;Database=webxemphim;User=webxemphim;Password=webxemphim;"
export Security__DataProtectionKeyPath="/root/keys"
export Security__StreamSigningKey="CinemaFlow_Phone_Stream_Signing_Key_2026_AtLeast32Bytes!"
export Payment__WebhookSecret="CinemaFlow_Phone_Payment_Webhook_Secret_2026_AtLeast32Bytes!"
export ApiBaseUrl="http://127.0.0.1:5085"
export Storage__MediaDirectory="/root/data/movies"
export Security__BehindReverseProxy=true

mkdir -p /root/data/movies /root/keys /root/app

echo "=== [4/5] Starting WebXemPhim.Server ==="
cd /root/app/server
nohup /opt/dotnet/dotnet WebXemPhim.Server.dll --urls "http://127.0.0.1:5085" --environment Development > /sdcard/server.log 2>&1 &
SERVER_PID=$!
echo "Server started (PID: $SERVER_PID)"

for i in {1..25}; do
    if curl -s http://127.0.0.1:5085/api/health >/dev/null 2>&1; then
        echo "Server is ready on port 5085"
        break
    fi
    sleep 1
done

echo "=== [5/5] Starting WebXemPhim.Client ==="
cd /root/app/client
nohup /opt/dotnet/dotnet WebXemPhim.Client.dll --urls "http://127.0.0.1:5229" --environment Development > /sdcard/client.log 2>&1 &
CLIENT_PID=$!
echo "Client started (PID: $CLIENT_PID)"

for i in {1..25}; do
    if curl -s http://127.0.0.1:5229 >/dev/null 2>&1; then
        echo "Client is ready on port 5229"
        break
    fi
    sleep 1
done

echo "=== Starting Caddy Reverse Proxy (Port 8080) ==="
caddy stop 2>/dev/null || true
caddy start --config /root/app/Caddyfile > /sdcard/caddy.log 2>&1
sleep 2

if curl -s http://127.0.0.1:8080 >/dev/null 2>&1; then
    echo "Caddy is UP and serving on port 8080!"
else
    echo "Warning: Caddy check returned non-zero, check /sdcard/caddy.log"
fi

echo "=== Starting Cloudflare Tunnel (Named Tunnel) ==="
CF_TOKEN="eyJhIjoiZjAwNzc1ZTc5NjY4OGVmZWQyMjVkYjAwODE1ZjdiODciLCJ0IjoiMzc5NzE2M2QtOGY3NS00MjQzLThkNmQtZGJmMGJjNzEzNGI4IiwicyI6Ik16azVPR1psWm1RdE1UUXdNUzAwWVdNekxXRmhORFl0WW1VMVlXVTJOekprTW1SayJ9"
nohup /usr/local/bin/cloudflared tunnel run --token "$CF_TOKEN" > /sdcard/cloudflared.log 2>&1 &
CF_PID=$!
echo "Cloudflared started (PID: $CF_PID)"

sleep 5
echo "=== Cloudflared Log Sample ==="
tail -n 12 /sdcard/cloudflared.log

echo "=== Final Process Check ==="
ps aux | grep -E 'WebXemPhim|dotnet|caddy|cloudflared|mariadbd' | grep -v grep

echo "=== Full CinemaFlow Stack Active on Phone! ==="
