#!/usr/bin/env bash
# ==============================================================================
# CinemaFlow (WebXemPhim) - VPS Production Initializer Script
# Hỗ trợ: Ubuntu 22.04 / 24.04 LTS, Debian 11 / 12
# ==============================================================================

set -euo pipefail

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

echo -e "${BLUE}===================================================================${NC}"
echo -e "${BLUE}     CinemaFlow - Khởi tạo môi trường VPS chạy Docker & Caddy     ${NC}"
echo -e "${BLUE}===================================================================${NC}"

# 1. Kiểm tra quyền root/sudo
if [ "$(id -u)" -ne 0 ]; then
    echo -e "${RED}[LỖI] Vui lòng chạy script này với quyền root hoặc sudo: sudo bash $0${NC}"
    exit 1
fi

# 2. Cập nhật hệ thống
echo -e "\n${YELLOW}>>> [1/5] Cập nhật danh sách gói hệ thống...${NC}"
apt-get update -y
apt-get install -y ca-certificates curl gnupg lsb-release ufw git openssl

# 3. Cài đặt Docker & Docker Compose Plugin (nếu chưa có)
echo -e "\n${YELLOW}>>> [2/5] Kiểm tra và cài đặt Docker Engine...${NC}"
if ! command -v docker &> /dev/null; then
    echo "Đang cài đặt Docker Engine từ repository chính thức..."
    install -m 0755 -d /etc/apt/keyrings
    curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc 2>/dev/null || \
    curl -fsSL https://download.docker.com/linux/debian/gpg -o /etc/apt/keyrings/docker.asc
    chmod a+r /etc/apt/keyrings/docker.asc

    DISTRO="ubuntu"
    if [ -f /etc/debian_version ] && ! grep -qi "ubuntu" /etc/os-release; then
        DISTRO="debian"
    fi

    echo \
      "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/${DISTRO} \
      $(. /etc/os-release && echo "${UBUNTU_CODENAME:-$VERSION_CODENAME}") stable" | \
      tee /etc/apt/sources.list.d/docker.list > /dev/null

    apt-get update -y
    apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
    systemctl enable --now docker
    echo -e "${GREEN}Đã cài đặt Docker thành công! Phiên bản: $(docker --version)${NC}"
else
    echo -e "${GREEN}Docker đã được cài đặt sẵn: $(docker --version)${NC}"
fi

# 4. Thiết lập tường lửa UFW (Mở cổng 22, 80, 443 TCP & UDP)
echo -e "\n${YELLOW}>>> [3/5] Cấu hình tường lửa UFW...${NC}"
ufw allow 22/tcp comment 'SSH' || true
ufw allow 80/tcp comment 'HTTP' || true
ufw allow 443/tcp comment 'HTTPS' || true
ufw allow 443/udp comment 'HTTP3 QUIC' || true
echo "y" | ufw enable || true

# 5. Khởi tạo thư mục triển khai
echo -e "\n${YELLOW}>>> [4/5] Khởi tạo thư mục triển khai ứng dụng...${NC}"
DEPLOY_DIR="${SUDO_USER:+/home/$SUDO_USER/cinemaflow}"
DEPLOY_DIR="${DEPLOY_DIR:-$HOME/cinemaflow}"
mkdir -p "$DEPLOY_DIR"

if [ -n "${SUDO_USER:-}" ]; then
    chown -R "$SUDO_USER:$SUDO_USER" "$DEPLOY_DIR"
    usermod -aG docker "$SUDO_USER" || true
fi

echo -e "${GREEN}Thư mục triển khai đã sẵn sàng tại: $DEPLOY_DIR${NC}"

# 6. Thông báo hoàn tất
echo -e "\n${BLUE}===================================================================${NC}"
echo -e "${GREEN}🎉 KHỞI TẠO VPS HOÀN TẤT THÀNH CÔNG!${NC}"
echo -e "${BLUE}===================================================================${NC}"
echo -e "Bước tiếp theo:"
echo -e "1. Thêm các GitHub Secrets vào Repository (Settings -> Secrets and variables -> Actions):"
echo -e "   - ${YELLOW}SERVER_HOST${NC}     : IP của VPS này ($(curl -s4 ifconfig.me 2>/dev/null || echo '<IP_VPS>'))"
echo -e "   - ${YELLOW}SERVER_USER${NC}     : ${SUDO_USER:-root}"
echo -e "   - ${YELLOW}SERVER_SSH_KEY${NC}  : Nội dung Private Key SSH kết nối vào VPS"
echo -e "   - ${YELLOW}SERVER_PORT${NC}     : 22 (mặc định)"
echo -e "2. Thêm Repository Variables (tùy chọn):"
echo -e "   - ${YELLOW}DOMAIN${NC}          : Tên miền của bạn (ví dụ: cinemaflow.vn)"
echo -e "   - ${YELLOW}ACME_EMAIL${NC}      : Email đăng ký SSL Let's Encrypt"
echo -e "3. Đẩy code lên nhánh main, GitHub Actions sẽ tự động test, build và triển khai!"
echo -e "${BLUE}===================================================================${NC}\n"
