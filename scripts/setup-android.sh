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
    curl wget git jq libicu-dev ca-certificates openssl procps tzdata postgresql caddy

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

echo -e "\n${YELLOW}>>> [3/6] Cấu hình Cơ sở dữ liệu PostgreSQL 17...${NC}"
# Khởi động dịch vụ PostgreSQL
/etc/init.d/postgresql restart || /etc/init.d/postgresql start

# Tạo User & Database
su - postgres -c "psql -c \"DO \$\$ BEGIN IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'webxemphim') THEN CREATE ROLE webxemphim WITH LOGIN SUPERUSER PASSWORD 'webxemphim'; END IF; END \$\$;\""
su - postgres -c "psql -tc \"SELECT 1 FROM pg_database WHERE datname = 'webxemphim'\" | grep -q 1 || psql -c \"CREATE DATABASE webxemphim OWNER webxemphim;\""

echo -e "\n${YELLOW}>>> [4/6] Tải mã nguồn CinemaFlow & Cài đặt Cloudflare Tunnel...${NC}"
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

# Tải Cloudflare Tunnel (cloudflared ARM64)
if [ ! -f "/usr/local/bin/cloudflared" ]; then
    echo "Đang tải cloudflared ARM64..."
    wget -q https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-linux-arm64 -O /usr/local/bin/cloudflared
    chmod +x /usr/local/bin/cloudflared
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
export Database__Provider=PostgreSql
export ConnectionStrings__Default="Host=localhost;Port=5432;Database=webxemphim;Username=webxemphim;Password=webxemphim"
export Security__StreamSigningKey="CinemaFlow_Phone_Stream_Signing_Key_2026_AtLeast32Bytes!"
export Payment__WebhookSecret="CinemaFlow_Phone_Payment_Webhook_Secret_2026_AtLeast32Bytes!"
export ApiBaseUrl="http://localhost:5085"

echo "=== [1/4] Khởi động PostgreSQL ==="
/etc/init.d/postgresql restart || /etc/init.d/postgresql start

echo "=== [2/4] Khởi động CinemaFlow Server & Client ==="
killall -9 dotnet 2>/dev/null || true
killall -9 caddy 2>/dev/null || true
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
echo -e "=== [4/4] Đang mở đường hầm Cloudflare ra Internet toàn cầu... ==="
echo -e "Đang lấy link HTTPS công khai..."
cloudflared tunnel --url http://localhost:8080
EOF

chmod +x /root/run.sh

cat << 'EOF' > /root/stop.sh
#!/usr/bin/env bash
echo "Đang dừng các dịch vụ CinemaFlow..."
killall -9 dotnet 2>/dev/null || true
killall -9 caddy 2>/dev/null || true
killall -9 cloudflared 2>/dev/null || true
/etc/init.d/postgresql stop || true
echo "Đã dừng toàn bộ dịch vụ thành công."
EOF
chmod +x /root/stop.sh

echo -e "\n${CYAN}====================================================================${NC}"
echo -e "${GREEN}🎉 CÀI ĐẶT HOÀN TẤT THÀNH CÔNG TRÊN ĐIỆN THOẠI!${NC}"
echo -e "${CYAN}====================================================================${NC}"
echo -e "Để khởi chạy hệ thống, bạn chỉ cần gõ lệnh:"
echo -e "   ${YELLOW}/root/run.sh${NC}"
echo -e "Hệ thống sẽ tự động in ra đường link HTTPS (ví dụ: https://xxx.trycloudflare.com) để bạn và bạn bè có thể xem phim từ bất kỳ đâu trên thế giới!"
echo -e "${CYAN}====================================================================${NC}\n"
