# CinemaFlow (WebXemPhim) - Hướng Dẫn Triển Khai Docker & Docker Compose

Tài liệu hướng dẫn triển khai hệ thống **CinemaFlow** lên môi trường container bằng Docker và Docker Compose cho cả môi trường phát triển (Development) và sản xuất (Production).

---

## 1. Kiến Trúc Container (Container Architecture)

Hệ thống được đóng gói thành các dịch vụ độc lập kết nối qua mạng nội bộ Docker bridge `cinemaflow-net`:

| Dịch Vụ | Container Name | Cổng Host : Container | Vai Trò & Công Nghệ | Volume Lưu Trữ |
| :--- | :--- | :--- | :--- | :--- |
| **Server** | `webxemphim-server` | `5085:8080` | ASP.NET Core Web API (.NET 10), SignalR Hub, Stream Signer, EF Core | `server_data` (`/app/data`), `server_keys` (`/app/keys`) |
| **Client** | `webxemphim-client` | `5229:8080` | Blazor Interactive Web App (.NET 10), CinemaPlayer, Taste-Skill UI | `client_keys` (`/app/keys`) |
| **PostgreSQL** | `webxemphim-postgres` | `5432:5432` | PostgreSQL 17 (database chính) | `postgres_data` (`/var/lib/postgresql/data`) |
| **MySQL** *(Tùy chọn)* | `webxemphim-mysql` | `3306:3306` | MySQL 8.4 LTS Server (kích hoạt khi chạy profile `--profile mysql`) | `mysql_data` (`/var/lib/mysql`) |

### Sơ Đồ Kết Nối Nội Bộ:

```
[ Trình Duyệt Người Dùng ]
      │
      ├──────► http://localhost:5229 (Blazor Client)
      │             │
      │             ▼ (Nội bộ Docker: http://server:8080)
      └──────► http://localhost:5085 (Web API & SignalR CinemaHub)
                    │
                    ├──► Volume postgres_data (PostgreSQL Database)
                    ├──► Volume /app/keys (ASP.NET DataProtection Keys)
                    └──► MySQL (nếu cấu hình MySql Provider)
```

---

## 2. Yêu Cầu Môi Trường (Prerequisites)

- **Docker Desktop** (Windows / macOS) hoặc **Docker Engine + Docker Compose v2** (Linux).
- Đảm bảo Docker Daemon đang hoạt động (trên Windows: mở ứng dụng Docker Desktop).

---

## 3. Khởi Chạy Nhanh (Quick Start - PostgreSQL Mode)

Đây là chế độ mặc định, tự động khởi tạo cơ sở dữ liệu PostgreSQL trong volume độc lập, nạp sẵn dữ liệu phim mẫu (Seed Data) và tự tạo khóa bảo mật.

### Bước 1: Mở Terminal tại thư mục gốc của dự án
```powershell
cd c:\Users\kynhu\Documents\Projects\wenPhim\w-xemphim
```

### Bước 2: Build và khởi động các container
```powershell
docker compose up -d --build
```

### Bước 3: Kiểm tra trạng thái
```powershell
docker compose ps
```

### Bước 4: Truy cập hệ thống
- **Giao diện người xem (Blazor Client)**: [http://localhost:5229](http://localhost:5229)
- **REST API Backend**: [http://localhost:5085/api/movies](http://localhost:5085/api/movies)
- **OpenAPI Document**: [http://localhost:5085/openapi/v1.json](http://localhost:5085/openapi/v1.json)
- **Tài khoản quản trị viên mặc định (Development)**: `admin@cinemaflow.vn` (hoặc `admin`) / `Password123!`

---

## 4. Triển Khai Với Cơ Sở Dữ Liệu PostgreSQL Bên Ngoài (Production Mode)

Khi muốn chạy với hệ quản trị cơ sở dữ liệu PostgreSQL do bạn quản lý
(RDS, Supabase, Neon, PostgreSQL Server nội bộ...):

### Bước 1: Sao chép tệp cấu hình môi trường
```powershell
copy .env.example .env
```

### Bước 2: Chỉnh tệp `.env`
Bật cấu hình PostgreSQL trong `.env`:
```ini
DB_PROVIDER=PostgreSql
DB_CONNECTION_STRING=Host=db-noi-boi.example.internal;Port=5432;Database=cinemaflow;Username=cinemaflow_user;Password=ChangeThisToStrongSecurePassword_2026!;SSL Mode=Require
```

### Bước 3: Tắt service PostgreSQL nội bộ, chỉ chạy API + Client
```powershell
docker compose up -d --build server client
```

> Ứng dụng tự áp dụng migration EF Core và nạp dữ liệu mẫu khi khởi động lần đầu.
> Nếu dùng bản production, đặt `SEED_DEMO_DATA=false` và `ASPNETCORE_ENVIRONMENT=Production`
> (khi đó server **bắt buộc** phải có `Security__StreamSigningKey`, `Payment__WebhookSecret`
> và `AllowedHosts` hợp lệ).

### Bước 4 (tùy chọn): Khởi tạo schema bằng script SQL thay vì để server tự migrate
```powershell
docker compose exec -T postgres psql -U webxemphim -d webxemphim < database/postgresql/001_initial.sql
```

---

## 5. Triển Khai Với Cơ Sở Dữ Liệu MySQL (Tùy Chọn)

MySQL 8.4 vẫn được hỗ trợ làm provider thứ hai:

### Bước 1: Sao chép tệp cấu hình môi trường
```powershell
copy .env.example .env
```

### Bước 2: Chỉnh tệp `.env`
Bật cấu hình MySQL trong `.env`:
```ini
DB_PROVIDER=MySql
DB_CONNECTION_STRING=Server=mysql;Port=3306;Database=webxemphim;User=cinemaflow;Password=cinemaflow_password_2026;
MYSQL_ROOT_PASSWORD=cinemaflow_root_secret_2026
MYSQL_DATABASE=webxemphim
MYSQL_USER=cinemaflow
MYSQL_PASSWORD=cinemaflow_password_2026
```

### Bước 3: Khởi chạy kèm profile `mysql`
```powershell
docker compose --profile mysql up -d --build
```

---

## 6. Các Lệnh Quản Trị Hữu Ích (Operations & Maintenance)

### Xem nhật ký thời gian thực (Live Logs)
```powershell
# Xem toàn bộ dịch vụ
docker compose logs -f

# Chỉ xem Server API
docker compose logs -f server

# Chỉ xem Client Web
docker compose logs -f client
```

### Dừng toàn bộ hệ thống
```powershell
docker compose down
```

### Dừng và xóa toàn bộ dữ liệu (Clean Reset)
> [!WARNING]
> Lệnh này sẽ xóa toàn bộ cơ sở dữ liệu và khóa bảo mật trong các named volumes.
```powershell
docker compose down -v
```

### Build lại sau khi thay đổi mã nguồn C#/Razor
```powershell
docker compose build
docker compose up -d
```

### Kiểm tra tính toàn vẹn của cấu hình Compose
```powershell
docker compose config
```

### Làm việc với PostgreSQL trong container
```powershell
# Mở shell psql
docker compose exec postgres psql -U webxemphim -d webxemphim

# Xem log của database
docker compose logs -f postgres
```

### Server không khởi động được sau khi đổi cấu hình
Server chỉ chạy sau khi PostgreSQL báo `healthy` (`depends_on: condition: service_healthy`).
Nếu server cứ restart, kiểm tra:
```powershell
docker compose logs server | Select-String "Name or service not known"
```
Lỗi `Name or service not known` nghĩa là container server chưa được gắn vào network
`cinemaflow-net`. Khi đó chạy lại:
```powershell
docker compose down
docker compose up -d
```


---

## 7. Tính Năng Bảo Mật Trong Môi Trường Container

1. **Persistent Data Protection**: Khóa mã hóa session và trường dữ liệu cá nhân (`FieldEncryptor`) được lưu trong volume riêng (`server_keys`, `client_keys`), không bị mất khi container restart hoặc upgrade image.
2. **Internal Communication Security**: Blazor Server giao tiếp trực tiếp với Web API qua DNS container `http://server:8080`, tránh lộ traffic nội bộ ra ngoài mạng công cộng.
3. **CORS & Forwarded Headers**: Server được cấu hình sẵn nhận diện dải IP nội bộ của Docker bridge (`172.16.0.0/12`), giúp xử lý đúng địa chỉ IP thực tế của client và ngăn chặn giả mạo tiêu đề `X-Forwarded-For`.
4. **Non-root / Least Privilege Ready**: Hình ảnh `mcr.microsoft.com/dotnet/aspnet:10.0` được tối ưu hóa cho môi trường container với cổng mặc định `8080`.
5. **PostgreSQL First**: Cơ sở dữ liệu chính là PostgreSQL 17 (service `postgres`, có healthcheck và volume `postgres_data`). Server tự áp dụng migration EF Core và nạp dữ liệu mẫu khi khởi động lần đầu. MySQL vẫn dùng được qua profile `mysql`.
