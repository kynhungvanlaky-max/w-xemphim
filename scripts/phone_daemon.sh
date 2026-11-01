#!/data/data/com.termux/files/usr/bin/bash
export PATH="/data/data/com.termux/files/usr/bin:$PATH"

termux-wake-lock 2>/dev/null || true

proot-distro login ubuntu -- bash -c '
echo "=== [1/6] Starting MariaDB Database ==="
if ! mariadb-admin ping >/dev/null 2>&1; then
    killall -9 mariadbd mariadbd-safe 2>/dev/null || true
    sleep 1
    nohup /usr/bin/mariadbd-safe --user=root > /sdcard/mariadb.log 2>&1 &
    for i in {1..25}; do
        if mariadb-admin ping >/dev/null 2>&1; then
            echo "MariaDB is UP and ready."
            break
        fi
        sleep 1
    done
else
    echo "MariaDB is already UP."
fi

echo "=== [2/6] Preparing .NET 10 Runtimes ==="
cd /opt/dotnet/shared/Microsoft.NETCore.App
ln -sfn 10.0.0-rc.2.25502.107 10.0.0 || true
cd /opt/dotnet/shared/Microsoft.AspNetCore.App
ln -sfn 10.0.0-rc.2.25502.107 10.0.0 || true

echo "=== [3/6] Stopping Previous Processes ==="
killall -9 WebXemPhim.Server 2>/dev/null || true
killall -9 WebXemPhim.Client 2>/dev/null || true
killall -9 caddy 2>/dev/null || true
killall -9 cloudflared 2>/dev/null || true
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
export Security__StreamSigningKey="***REMOVED***"
export Payment__WebhookSecret="***REMOVED***"
export ApiBaseUrl="http://127.0.0.1:5085"
export Storage__MediaDirectory="/root/data/movies"
export Security__BehindReverseProxy=true

mkdir -p /root/data/movies /root/keys /root/app

echo "=== [4/6] Starting WebXemPhim.Server ==="
cd /root/app/server
nohup /opt/dotnet/dotnet WebXemPhim.Server.dll --urls "http://127.0.0.1:5085" --environment Development > /sdcard/server.log 2>&1 &
SERVER_PID=$!
echo "WebXemPhim.Server running (PID: $SERVER_PID)"

for i in {1..30}; do
    if curl -s http://127.0.0.1:5085/api/health >/dev/null 2>&1; then
        echo "Server healthy on port 5085"
        break
    fi
    sleep 1
done

echo "=== [5/6] Starting WebXemPhim.Client (Blazor) ==="
cd /root/app/client
nohup /opt/dotnet/dotnet WebXemPhim.Client.dll --urls "http://127.0.0.1:5229" --environment Development > /sdcard/client.log 2>&1 &
CLIENT_PID=$!
echo "WebXemPhim.Client running (PID: $CLIENT_PID)"

for i in {1..30}; do
    if curl -s http://127.0.0.1:5229 >/dev/null 2>&1; then
        echo "Client healthy on port 5229"
        break
    fi
    sleep 1
done

echo "=== [6/6] Starting Caddy & Cloudflare Tunnel ==="
caddy stop 2>/dev/null || true
caddy start --config /root/app/Caddyfile > /sdcard/caddy.log 2>&1
sleep 2

if curl -s http://127.0.0.1:8080 >/dev/null 2>&1; then
    echo "Caddy Gateway active on http://127.0.0.1:8080"
fi

CF_TOKEN="***REMOVED***"
rm -f /sdcard/cloudflared.log
nohup /usr/local/bin/cloudflared tunnel run --token "$CF_TOKEN" > /sdcard/cloudflared.log 2>&1 &
CF_PID=$!
echo "Cloudflare Tunnel running (PID: $CF_PID)"

sleep 4
echo ""
echo "=========================================================="
echo "🎬 CINEMAFLOW PHONE HOST IS FULLY ONLINE & OPERATIONAL! 🚀"
echo "=========================================================="
echo "• Local Gateway: http://127.0.0.1:8080"
echo "• Backend API:   http://127.0.0.1:5085"
echo "• Frontend UI:   http://127.0.0.1:5229"
echo "• Tunnel ID:     3797163d-8f75-4243-8d6d-dbf0bc7134b8"
echo "• Cloudflare Ingress Point: http://localhost:8080"
echo "=========================================================="
echo "Streaming logs from Cloudflare Tunnel (Press Ctrl+C to stop log view):"
echo ""

tail -f /sdcard/cloudflared.log
'
