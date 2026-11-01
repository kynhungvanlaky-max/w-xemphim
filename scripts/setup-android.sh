#!/usr/bin/env bash
# ==============================================================================
# CinemaFlow (WebXemPhim) - Android (POCO / Snapdragon 855+) Setup Script
# Chạy trong Termux / Ubuntu PRoot ARM64
# ==============================================================================

set -euo pipefail

CYAN='\033[0;36m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

echo -e "${CYAN}====================================================================${NC}"
echo -e "${CYAN}     CinemaFlow - Khởi tạo Web Server trên Điện Thoại Android      ${NC}"
echo -e "${CYAN}====================================================================${NC}"

# Kiểm tra nếu đang chạy ở ngoài Termux thông thường (chưa vào Ubuntu PRoot)
if [ -d "/data/data/com.termux" ] && [ ! -f "/etc/os-release" ]; then
    echo -e "${YELLOW}>>> Phát hiện đang chạy ở Termux gốc. Đang chuẩn bị môi trường Ubuntu ARM64...${NC}"
    pkg update -y
    pkg install -y proot-distro git curl wget
    if ! proot-distro list | grep -q "ubuntu.*installed"; then
        echo -e "${YELLOW}>>> Đang tải và cài đặt Ubuntu 24.04 ARM64...${NC}"
        proot-distro install ubuntu
    fi
    echo -e "${GREEN}>>> Đang chuyển tiếp vào Ubuntu để hoàn tất cài đặt...${NC}"
    proot-distro login ubuntu -- bash -c "curl -fsSL https://raw.githubusercontent.com/kynhungvanlaky-max/w-xemphim/main/scripts/setup-android.sh | bash"
    exit 0
fi

# ==============================================================================
# BÊN TRONG UBUNTU PROOT (ARM64)
# ==============================================================================
echo -e "\n${YELLOW}>>> [1/6] Cập nhật Ubuntu & cài đặt các gói phụ thuộc...${NC}"
apt-get update -y
DEBIAN_FRONTEND=noninteractive apt-get install -y \
    curl wget git jq libicu-dev ca-certificates openssl procps tzdata mariadb-server caddy

# Cấu hình múi giờ Việt Nam
ln -fs /usr/share/zoneinfo/Asia/Ho_Chi_Minh /etc/localtime || true
dpkg-reconfigure --frontend noninteractive tzdata 2>/dev/null || true

echo -e "\n${YELLOW}>>> [2/6] Cài đặt Microsoft .NET 10 SDK (Linux ARM64)...${NC}"
export DOTNET_ROOT=/opt/dotnet
if [ ! -f "$DOTNET_ROOT/dotnet" ]; then
    mkdir -p "$DOTNET_ROOT"
    curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --quality preview --install-dir "$DOTNET_ROOT"
fi
export PATH="$DOTNET_ROOT:$PATH"
echo 'export DOTNET_ROOT=/opt/dotnet' >> /root/.bashrc
echo 'export PATH="$DOTNET_ROOT:$PATH"' >> /root/.bashrc

echo -e "${GREEN}Phiên bản .NET: $($DOTNET_ROOT/dotnet --version)${NC}"

echo -e "\n${YELLOW}>>> [3/6] Cấu hình Cơ sở dữ liệu MariaDB...${NC}"
# Khởi động dịch vụ MariaDB
/etc/init.d/mariadb start || true
for i in {1..30}; do
    if mariadb-admin ping >/dev/null 2>&1; then
        echo "MariaDB đã khởi động thành công!"
        break
    fi
    sleep 1
done

# Tạo User & Database
mariadb -e "CREATE DATABASE IF NOT EXISTS webxemphim CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;" 2>/dev/null || true
mariadb -e "CREATE USER IF NOT EXISTS 'webxemphim'@'%' IDENTIFIED BY 'webxemphim';" 2>/dev/null || true
mariadb -e "GRANT ALL PRIVILEGES ON webxemphim.* TO 'webxemphim'@'%';" 2>/dev/null || true
mariadb -e "CREATE USER IF NOT EXISTS 'webxemphim'@'localhost' IDENTIFIED BY 'webxemphim';" 2>/dev/null || true
mariadb -e "GRANT ALL PRIVILEGES ON webxemphim.* TO 'webxemphim'@'localhost';" 2>/dev/null || true
mariadb -e "CREATE USER IF NOT EXISTS 'webxemphim'@'127.0.0.1' IDENTIFIED BY 'webxemphim';" 2>/dev/null || true
mariadb -e "GRANT ALL PRIVILEGES ON webxemphim.* TO 'webxemphim'@'127.0.0.1';" 2>/dev/null || true
mariadb -e "FLUSH PRIVILEGES;" 2>/dev/null || true

echo -e "\n${YELLOW}>>> [4/6] Tải mã nguồn CinemaFlow & Cài đặt ngrok Tunnel...${NC}"
cd /root
if [ ! -d "w-xemphim" ]; then
    git clone https://github.com/kynhungvanlaky-max/w-xemphim.git
else
    cd w-xemphim && git pull && cd /root
fi

cd /root/w-xemphim

# Nhập dữ liệu khởi tạo nếu bảng phim chưa có
echo "Đang đồng bộ dữ liệu mẫu vào PostgreSQL..."
su - postgres -c "psql -U webxemphim -d webxemphim -f /root/w-xemphim/database/postgresql/001_initial.sql" 2>/dev/null || true

# Tải ngrok Tunnel (Linux ARM64 chính thức)
if [ ! -f "/usr/local/bin/ngrok" ]; then
    echo "Đang tải ngrok Linux ARM64..."
    wget -q https://bin.equinox.io/c/bNyj1mQVY4c/ngrok-v3-stable-linux-arm64.tgz -O /tmp/ngrok.tgz
    tar -xzf /tmp/ngrok.tgz -C /usr/local/bin/
    rm -f /tmp/ngrok.tgz
    chmod +x /usr/local/bin/ngrok
fi

echo -e "\n${YELLOW}>>> [5/6] Biên dịch ứng dụng (Release ARM64)...${NC}"
$DOTNET_ROOT/dotnet publish src/Server/WebXemPhim.Server/WebXemPhim.Server.csproj -c Release -o /root/app/server
$DOTNET_ROOT/dotnet publish src/Client/WebXemPhim.Client/WebXemPhim.Client.csproj -c Release -o /root/app/client

# Cấu hình Caddyfile cho Android
cat << 'EOF' > /root/app/Caddyfile
:8080 {
    encode zstd gzip

    handle /hubs/* {
        reverse_proxy localhost:5085
    }
    handle /api/* {
        reverse_proxy localhost:5085
    }
    handle /openapi/* {
        reverse_proxy localhost:5085
    }
    handle {
        reverse_proxy localhost:5229
    }
}
EOF

echo -e "\n${YELLOW}>>> [6/6] Tạo kịch bản khởi chạy hệ thống (run.sh)...${NC}"
cat << 'EOF' > /root/run.sh
#!/usr/bin/env bash
set -e

export DOTNET_ROOT=/opt/dotnet
export PATH="$DOTNET_ROOT:$PATH"
export ASPNETCORE_ENVIRONMENT=Production
export AllowedHosts="*"
export Security__BehindReverseProxy=true
export Database__Provider=MySql
export ConnectionStrings__Default="Server=127.0.0.1;Port=3306;Database=webxemphim;User=webxemphim;Password=webxemphim;"
export Security__DataProtectionKeyPath="/root/keys"
export Security__StreamSigningKey="***REMOVED***"
export Payment__WebhookSecret="***REMOVED***"
export ApiBaseUrl="http://localhost:5085"

mkdir -p /root/keys /root/data /root/app

echo "=== [1/4] Khởi động MariaDB ==="
/etc/init.d/mariadb start || true
for i in {1..15}; do
    if mariadb-admin ping >/dev/null 2>&1; then
        break
    fi
    sleep 1
done

echo "=== [2/4] Khởi động CinemaFlow Server & Client ==="
killall -9 dotnet 2>/dev/null || true
killall -9 caddy 2>/dev/null || true
killall -9 ngrok 2>/dev/null || true
killall -9 cloudflared 2>/dev/null || true

cd /root/app/server
ASPNETCORE_URLS=http://localhost:5085 ./WebXemPhim.Server > /root/app/server.log 2>&1 &

cd /root/app/client
ASPNETCORE_URLS=http://localhost:5229 ./WebXemPhim.Client > /root/app/client.log 2>&1 &

sleep 3

echo "=== [3/4] Khởi động Caddy Reverse Proxy (Port 8080) ==="
cd /root/app
caddy start --config /root/app/Caddyfile > /dev/null 2>&1

echo -e "\n===================================================================="
echo -e "🎉 CinemaFlow ĐANG CHẠY TRỰC TIẾP TRÊN CHIP SNAPDRAGON 855+!"
echo -e "===================================================================="
echo -e "Truy cập nội bộ mạng Wi-Fi: http://$(hostname -I | awk '{print $1}'):8080"
echo -e "=== [4/4] Đang mở đường hầm ngrok ra Internet toàn cầu... ==="
if [ -f "/sdcard/ngrok_token.txt" ]; then
    NGROK_TOKEN=$(cat /sdcard/ngrok_token.txt | tr -d '\r\n ')
    if [ -n "$NGROK_TOKEN" ]; then
        ngrok config add-authtoken "$NGROK_TOKEN" >/dev/null 2>&1 || true
    fi
fi
rm -f /sdcard/ngrok.log /sdcard/tunnel_url.txt
nohup ngrok http 8080 --log=stdout > /sdcard/ngrok.log 2>&1 &
echo "Đang lấy link HTTPS công khai từ ngrok..."
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

if [ -n "$TUNNEL_URL" ]; then
    echo -e "🌐 PUBLIC NGROK URL: $TUNNEL_URL"
    echo "$TUNNEL_URL" > /sdcard/tunnel_url.txt
else
    echo -e "⚠️ Chưa nhận được URL ngrok tự động. Kiểm tra file log: /sdcard/ngrok.log hoặc cấu hình token: ngrok config add-authtoken <TOKEN>"
fi
EOF

chmod +x /root/run.sh

cat << 'EOF' > /root/stop.sh
#!/usr/bin/env bash
echo "Đang dừng các dịch vụ CinemaFlow..."
killall -9 dotnet 2>/dev/null || true
killall -9 caddy 2>/dev/null || true
killall -9 ngrok 2>/dev/null || true
killall -9 cloudflared 2>/dev/null || true
/etc/init.d/mariadb stop || true
echo "Đã dừng toàn bộ dịch vụ thành công."
EOF
chmod +x /root/stop.sh

echo -e "\n${CYAN}====================================================================${NC}"
echo -e "${GREEN}🎉 CÀI ĐẶT HOÀN TẤT THÀNH CÔNG TRÊN ĐIỆN THOẠI VỚI NGROK!${NC}"
echo -e "${CYAN}====================================================================${NC}"
echo -e "Để khởi chạy hệ thống, bạn chỉ cần gõ lệnh:"
echo -e "   ${YELLOW}/root/run.sh${NC}"
echo -e "Hệ thống sẽ tự động in ra đường link HTTPS ngrok (ví dụ: https://xxx.ngrok-free.app) để bạn truy cập từ bất kỳ đâu!"
echo -e "${CYAN}====================================================================${NC}\n"
