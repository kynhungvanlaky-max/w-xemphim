#!/usr/bin/env bash
# ==============================================================================
# CinemaFlow Host Runner for Android PRoot Ubuntu
# ==============================================================================
set -e

echo "=== [1/6] Khởi động MariaDB Server ==="
/etc/init.d/mariadb start || true

for i in {1..30}; do
    if mariadb-admin ping >/dev/null 2>&1; then
        echo "MariaDB đã khởi động thành công!"
        break
    fi
    sleep 1
done

# Tạo database và user webxemphim
mariadb -e "CREATE DATABASE IF NOT EXISTS webxemphim CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;" 2>/dev/null || true
mariadb -e "CREATE USER IF NOT EXISTS 'webxemphim'@'%' IDENTIFIED BY 'webxemphim';" 2>/dev/null || true
mariadb -e "GRANT ALL PRIVILEGES ON webxemphim.* TO 'webxemphim'@'%';" 2>/dev/null || true
mariadb -e "CREATE USER IF NOT EXISTS 'webxemphim'@'localhost' IDENTIFIED BY 'webxemphim';" 2>/dev/null || true
mariadb -e "GRANT ALL PRIVILEGES ON webxemphim.* TO 'webxemphim'@'localhost';" 2>/dev/null || true
mariadb -e "CREATE USER IF NOT EXISTS 'webxemphim'@'127.0.0.1' IDENTIFIED BY 'webxemphim';" 2>/dev/null || true
mariadb -e "GRANT ALL PRIVILEGES ON webxemphim.* TO 'webxemphim'@'127.0.0.1';" 2>/dev/null || true
mariadb -e "FLUSH PRIVILEGES;" 2>/dev/null || true

echo "=== [2/6] Cập nhật nhị phân CinemaFlow ARM64 ==="
mkdir -p /root/app /root/keys /root/data
if [ -f "/sdcard/cinemaflow-arm64.tar.gz" ] && [ ! -d "/root/app/server" ]; then
    echo "Giải nén gói ứng dụng ban đầu..."
    tar -xzf /sdcard/cinemaflow-arm64.tar.gz -C /root/app/
fi

# Copy các file DLL mới cập nhật từ PC sang
if [ -f "/sdcard/WebXemPhim.Server.dll" ]; then
    cp -f /sdcard/WebXemPhim.Server.dll /root/app/server/WebXemPhim.Server.dll
fi
if [ -f "/sdcard/WebXemPhim.Client.dll" ]; then
    cp -f /sdcard/WebXemPhim.Client.dll /root/app/client/WebXemPhim.Client.dll
fi
if [ -f "/sdcard/WebXemPhim.Shared.dll" ]; then
    cp -f /sdcard/WebXemPhim.Shared.dll /root/app/server/WebXemPhim.Shared.dll
    cp -f /sdcard/WebXemPhim.Shared.dll /root/app/client/WebXemPhim.Shared.dll
fi
if [ -d "/sdcard/client_wwwroot" ]; then
    mkdir -p /root/app/client/wwwroot
    cp -rf /sdcard/client_wwwroot/* /root/app/client/wwwroot/ 2>/dev/null || true
fi
if [ -d "/sdcard/wwwroot" ]; then
    mkdir -p /root/app/client/wwwroot
    cp -rf /sdcard/wwwroot/* /root/app/client/wwwroot/ 2>/dev/null || true
fi
for f in /sdcard/*.staticwebassets.endpoints.json; do
    if [ -f "$f" ]; then
        cp -f "$f" /root/app/client/ 2>/dev/null || true
    fi
done

chmod +x /root/app/server/WebXemPhim.Server
chmod +x /root/app/client/WebXemPhim.Client

echo "=== [3/6] Cấu hình Caddy ==="
cat << 'EOF' > /root/app/Caddyfile
:8080 {
    encode zstd gzip

    handle /hubs/* {
        reverse_proxy 127.0.0.1:5085
    }
    handle /api/* {
        reverse_proxy 127.0.0.1:5085
    }
    handle /openapi/* {
        reverse_proxy 127.0.0.1:5085
    }
    handle_path /media/* {
        root * /root/data/movies
        file_server
    }
    handle {
        reverse_proxy 127.0.0.1:5229
    }
}
EOF

echo "=== [4/6] Dọn dẹp tiến trình cũ ==="
killall -9 WebXemPhim.Server 2>/dev/null || true
killall -9 WebXemPhim.Client 2>/dev/null || true
killall -9 caddy 2>/dev/null || true
killall -9 ngrok 2>/dev/null || true
killall -9 cloudflared 2>/dev/null || true

# Đảm bảo ngrok ARM64 đã được cài đặt
if [ ! -f "/usr/local/bin/ngrok" ]; then
    echo "Đang cài đặt ngrok Linux ARM64..."
    wget -q https://bin.equinox.io/c/bNyj1mQVY4c/ngrok-v3-stable-linux-arm64.tgz -O /tmp/ngrok.tgz
    tar -xzf /tmp/ngrok.tgz -C /usr/local/bin/
    rm -f /tmp/ngrok.tgz
    chmod +x /usr/local/bin/ngrok
fi

# Tự động nạp Authtoken ngrok nếu có file /sdcard/ngrok_token.txt
if [ -f "/sdcard/ngrok_token.txt" ]; then
    NGROK_TOKEN=$(cat /sdcard/ngrok_token.txt | tr -d '\r\n ')
    if [ -n "$NGROK_TOKEN" ]; then
        ngrok config add-authtoken "$NGROK_TOKEN" >/dev/null 2>&1 || true
    fi
fi

echo "=== [5/6] Khởi chạy CinemaFlow Server & Client ==="
export ASPNETCORE_ENVIRONMENT=Development
export DOTNET_ENVIRONMENT=Development
export AllowedHosts="*"
export Security__BehindReverseProxy=true
export Database__Provider=MySql
export Database__SeedDemoData=true
export ConnectionStrings__Default="Server=127.0.0.1;Port=3306;Database=webxemphim;User=webxemphim;Password=webxemphim;"
export Security__DataProtectionKeyPath="/root/keys"
export Security__StreamSigningKey="CinemaFlow_Phone_Stream_Signing_Key_2026_AtLeast32Bytes!"
export Payment__WebhookSecret="CinemaFlow_Phone_Payment_Webhook_Secret_2026_AtLeast32Bytes!"
export ApiBaseUrl="http://127.0.0.1:5085"
export Storage__MediaDirectory="/root/data/movies"

cd /root/app/server
nohup ./WebXemPhim.Server --urls "http://127.0.0.1:5085" --environment Development > /sdcard/server.log 2>&1 &
SERVER_PID=$!
echo "Server đang khởi chạy (PID: $SERVER_PID, Port: 5085)..."

for i in {1..30}; do
    if curl -s http://127.0.0.1:5085/api/health >/dev/null 2>&1 || curl -s http://127.0.0.1:5085/ >/dev/null 2>&1; then
        echo "Server đã sẵn sàng (Port: 5085)!"
        break
    fi
    sleep 1
done

cd /root/app/client
nohup ./WebXemPhim.Client --urls "http://127.0.0.1:5229" --environment Development > /sdcard/client.log 2>&1 &
CLIENT_PID=$!
echo "Client đang khởi chạy (PID: $CLIENT_PID, Port: 5229)..."

for i in {1..30}; do
    if curl -s http://127.0.0.1:5229/ >/dev/null 2>&1; then
        echo "Client đã sẵn sàng (Port: 5229)!"
        break
    fi
    sleep 1
done

caddy start --config /root/app/Caddyfile > /sdcard/caddy.log 2>&1
echo "Caddy reverse proxy đã khởi chạy (Port: 8080)"

sleep 2

rm -f /sdcard/ngrok.log /sdcard/tunnel_url.txt
nohup ngrok http 8080 --log=stdout > /sdcard/ngrok.log 2>&1 &
NGROK_PID=$!
echo "ngrok Tunnel đang khởi động (PID: $NGROK_PID)..."

echo "=== [6/6] Đang lấy Public ngrok URL ==="
TUNNEL_URL=""
for i in {1..35}; do
    TUNNEL_URL=$(curl -s http://127.0.0.1:4040/api/tunnels 2>/dev/null | jq -r '.tunnels[]? | select(.proto=="https") | .public_url' | head -n 1 || true)
    if [ -z "$TUNNEL_URL" ]; then
        TUNNEL_URL=$(grep -o 'https://[a-zA-Z0-9.-]*\.ngrok-free\.app' /sdcard/ngrok.log 2>/dev/null | head -n 1 || true)
    fi
    if [ -n "$TUNNEL_URL" ]; then
        break
    fi
    sleep 1
done

IP=$(hostname -I 2>/dev/null | awk '{print $1}')
echo -e "\n===================================================================="
echo -e "🎉 CinemaFlow ĐANG CHẠY TRỰC TIẾP TRÊN ĐIỆN THOẠI ANDROID!"
echo -e "IP nội bộ Wi-Fi: http://${IP:-127.0.0.1}:8080"
if [ -n "$TUNNEL_URL" ]; then
    echo -e "🌐 PUBLIC NGROK URL: $TUNNEL_URL"
    echo "$TUNNEL_URL" > /sdcard/tunnel_url.txt
else
    echo -e "⚠️ Chưa nhận được URL ngrok tự động."
    echo -e "Vui lòng xem file log tại /sdcard/ngrok.log hoặc cấu hình token bằng lệnh: ngrok config add-authtoken <TOKEN>"
fi
echo -e "===================================================================="

wait
