# Hướng Dẫn Triển Khai CinemaFlow Lên VPS Qua GitHub Actions & Docker

Tài liệu này hướng dẫn chi tiết quy trình đưa dự án **CinemaFlow (WebXemPhim)** lên môi trường Internet thực tế (Production) bằng **Docker Compose, Caddy (Tự động cấp SSL/HTTPS)** và quy trình **CI/CD tự động từ GitHub Actions**.

---

## 1. Sơ Đồ Kiến Trúc Triển Khai (Production Architecture)

```
[ Người Dùng Ngoài Internet ]
              │
              ▼ (Cổng 80/443 - Tự động cấp SSL Let's Encrypt / HTTP3)
     ┌───────────────────────────────────────────────┐
     │            Caddy Reverse Proxy                │
     └───────┬───────────────────────────────┬───────┘
             │                               │
             ▼                               ▼
    [ /api/*, /hubs/*, /openapi/* ]     [ Mọi route khác & /_blazor ]
             │                               │
             ▼                               ▼
┌─────────────────────────┐     ┌─────────────────────────┐
│ webxemphim-server (API) │     │ webxemphim-client (UI)  │
│      Cổng 8080 nội bộ   │◄────┤      Cổng 8080 nội bộ   │
└────────────┬────────────┘     └─────────────────────────┘
             │
             ▼
┌─────────────────────────┐
│ webxemphim-postgres     │
│   (PostgreSQL 17)       │
└─────────────────────────┘
```

> **Ưu điểm vượt trội của mô hình này:**
> - **Chung một Domain duy nhất**: Không cần tạo nhiều subdomain rườm rà.
> - **Triệt tiêu lỗi CORS**: Cả Client và API cùng chung origin domain.
> - **Tự động gia hạn SSL miễn phí**: Caddy tự lo chứng chỉ Let's Encrypt mà không cần cấu hình Certbot.
> - **CI/CD hoàn toàn tự động**: Đẩy code lên nhánh `main`, GitHub Actions sẽ tự test, build container image đưa lên GHCR và SSH vào server cập nhật phiên bản mới nhất!

---

## 2. Yêu Cầu Chuẩn Bị VPS

- **Cấu hình đề xuất**: VPS tối thiểu 1 vCPU, 2GB RAM (khuyến nghị 2 vCPU, 4GB RAM cho trải nghiệm mượt mà).
- **Hệ điều hành**: Ubuntu 22.04 LTS / 24.04 LTS hoặc Debian 11 / 12.
- **Nhà cung cấp phổ biến**: Hetzner, DigitalOcean, Linode/Akamai, AWS EC2, OVH, hoặc các nhà cung cấp VPS tại Việt Nam (Vietnix, BKNS, TinoHost...).

---

## 3. Các Bước Triển Khai Nhanh

### Bước 1: Khởi Tạo VPS (Chạy 1 lệnh duy nhất)

Kết nối SSH vào VPS của bạn qua terminal:
```bash
ssh root@<IP_VPS_CỦA_BẠN>
```

Chạy lệnh cài đặt tự động (Cài đặt Docker Engine, Docker Compose Plugin và cấu hình tường lửa UFW):
```bash
curl -fsSL https://raw.githubusercontent.com/kynhungvanlaky-max/w-xemphim/main/scripts/setup-vps.sh | bash
```

Sau khi chạy xong, thư mục `~/cinemaflow` sẽ được khởi tạo sẵn sàng trên VPS.

---

### Bước 2: Trỏ Tên Miền (DNS) Về VPS

Truy cập trang quản trị tên miền của bạn (Cloudflare, Namecheap, GoDaddy, Pavietnam...) và thêm bản ghi:
- **Loại (Type)**: `A`
- **Tên (Name)**: `@` (hoặc subdomain như `phim`)
- **Giá trị (Value)**: Địa chỉ `<IP_VPS_CỦA_BẠN>`
- **Proxy Status** (nếu dùng Cloudflare): Khuyến nghị để **DNS only** (biểu tượng đám mây màu xám) trong lần đầu để Caddy dễ dàng xác thực chứng chỉ SSL Let's Encrypt.

---

### Bước 3: Cấu Hình GitHub Secrets & Variables

Truy cập repository GitHub của bạn:
👉 **Settings** ➔ **Secrets and variables** ➔ **Actions**

#### 🔑 Thêm các Repository Secrets:
Bấm **New repository secret**:
1. `SERVER_HOST`: Địa chỉ IP của VPS (ví dụ: `103.12.34.56`).
2. `SERVER_USER`: Tên tài khoản SSH trên VPS (thường là `root` hoặc `ubuntu`).
3. `SERVER_SSH_KEY`: Nội dung **Private Key SSH** dùng để đăng nhập vào VPS (chuỗi bắt đầu từ `-----BEGIN OPENSSH PRIVATE KEY-----` đến `-----END OPENSSH PRIVATE KEY-----`).
4. `SERVER_PORT`: Cổng SSH (mặc định là `22`).

> **Mẹo tạo SSH Key nếu chưa có:**
> Trên máy của bạn hoặc trên VPS, chạy:
> ```bash
> ssh-keygen -t ed25519 -C "github-actions-deploy"
> cat ~/.ssh/id_ed25519.pub >> ~/.ssh/authorized_keys
> chmod 600 ~/.ssh/authorized_keys
> cat ~/.ssh/id_ed25519
> ```
> Sao chép toàn bộ nội dung xuất ra từ lệnh `cat ~/.ssh/id_ed25519` để dán vào `SERVER_SSH_KEY`.

#### 🌐 Thêm Repository Variables (Tùy chọn nhưng khuyến nghị):
Chuyển sang tab **Variables** ➔ Bấm **New repository variable**:
1. `DOMAIN`: Tên miền của bạn (ví dụ: `cinemaflow.vn` hoặc `phim.example.com`). Nếu chưa có tên miền, có thể điền IP của VPS.
2. `ACME_EMAIL`: Email của bạn để nhận thông báo gia hạn chứng chỉ SSL từ Let's Encrypt (ví dụ: `admin@cinemaflow.vn`).

---

### Bước 4: Kích Hoạt Triển Khai Tự Động

Mọi thứ đã sẵn sàng! Bây giờ bạn chỉ cần:
1. Đẩy code lên nhánh `main`:
   ```bash
   git push origin main
   ```
   *Hoặc truy cập tab **Actions** trên GitHub ➔ Chọn workflow **CinemaFlow CI/CD** ➔ Bấm **Run workflow**.*

2. GitHub Actions sẽ tự động thực hiện:
   - **Job 1 (Test)**: Chạy toàn bộ 1,312 bài kiểm thử tự động.
   - **Job 2 (Build & Push)**: Đóng gói Docker images của Server và Client, đẩy lên **GitHub Container Registry (ghcr.io)**.
   - **Job 3 (Deploy)**: SSH vào VPS, đồng bộ cấu hình, kéo image mới nhất về và khởi chạy hệ thống bằng Docker Compose!

---

## 4. Kiểm Tra & Truy Cập

Sau khi GitHub Actions báo xanh (Success):
- Mở trình duyệt và truy cập: `https://<TÊN_MIỀN_CỦA_BẠN>`
- Bạn sẽ thấy giao diện **CinemaFlow** tải nhanh chóng với biểu tượng khóa SSL màu xanh an toàn!

---

## 5. Các Lệnh Vận Hành & Bảo Trì Hữu Ích Trên VPS

Khi SSH vào VPS (`cd ~/cinemaflow`):

### Xem trạng thái các container:
```bash
docker compose -f docker-compose.prod.yml ps
```

### Xem trực tiếp nhật ký (Live Logs):
```bash
# Xem log toàn bộ hệ thống
docker compose -f docker-compose.prod.yml logs -f

# Chỉ xem log của API Server
docker compose -f docker-compose.prod.yml logs -f server

# Chỉ xem log của Caddy (kiểm tra SSL)
docker compose -f docker-compose.prod.yml logs -f caddy
```

### Khởi động lại hoặc cập nhật thủ công:
```bash
docker compose -f docker-compose.prod.yml pull
docker compose -f docker-compose.prod.yml up -d
```

### Sao lưu cơ sở dữ liệu PostgreSQL (Backup):
```bash
docker compose -f docker-compose.prod.yml exec -T postgres pg_dump -U webxemphim webxemphim > backup_$(date +%Y%m%d_%H%M%S).sql
```

### Phục hồi cơ sở dữ liệu (Restore):
```bash
docker compose -f docker-compose.prod.yml exec -T postgres psql -U webxemphim webxemphim < backup_file.sql
```
