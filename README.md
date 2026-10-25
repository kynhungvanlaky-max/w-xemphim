# WebXemPhim - Nền Tảng Xem Phim Trực Tuyến Hiện Đại

Ứng dụng xem phim trực tuyến thế hệ mới được xây dựng bằng kiến trúc phân tách hoàn toàn giữa **Server (ASP.NET Core Web API)** và **Client (Blazor Web App)** với ngôn ngữ C# (.NET 10). Giao diện thiết kế theo phong cách điện ảnh cao cấp (Cinema-grade Obsidian), hỗ trợ mượt mà cả hai chế độ **Dark Mode** và **Light Mode**.

---

## 🌟 Tính Năng Nổi Bật

### 🎬 Trải Nghiệm Người Dùng (Client)
- **Rạp chiếu tại gia**: Trình phát phim thông minh tích hợp công cụ chọn nguồn CDN, hiển thị ping máy chủ thời gian thực, điều khiển phím tắt tiện lợi.
- **Phân vùng Bento Box thể loại**: Thiết kế dạng Bento Grid nghệ thuật với hình ảnh điện ảnh sắc nét, tương tác mượt mà.
- **Bộ lọc & Khám phá phim thông minh**: Tìm kiếm tức thì, lọc theo thể loại, định dạng (phim lẻ / phim bộ), quốc gia, năm phát hành, điểm IMDb.
- **Hệ thống đánh giá & bình luận**: Người dùng có thể chấm điểm, viết nhận xét và nhận phản hồi trực tiếp từ ban quản trị.
- **Hội viên VIP & Thanh toán**: Hỗ trợ nâng cấp các gói hội viên (Silver, Gold, Platinum, Diamond) với cổng thanh toán mô phỏng tiện ích.
- **Danh sách theo dõi & Lịch sử xem**: Lưu phim yêu thích và ghi nhớ vị trí phát của tập phim đã xem.
- **Dual-theme tinh tế**: Tối ưu độ tương phản và thẩm mỹ thị giác tuyệt đối cho cả Dark Mode (Obsidian `#07080a`) và Light Mode (`#ffffff`).

### 🛡️ Phân Hệ Quản Trị Chuyên Sâu (Admin Workspace)
- **Quản lý kho phim (`/admin/movies`)**: Bộ lọc 7 chiều (thể loại, định dạng, chất lượng 4K/FHD, cờ thịnh hành, năm, IMDb, tìm kiếm từ khóa).
- **Quản lý tập phim (`/admin/episodes`)**: Quản lý đa tập, tải lên ảnh nền tập tùy chỉnh, kiểm soát độ sẵn sàng của các máy chủ CDN luồng stream.
- **Quản lý người dùng (`/admin/users`)**: Thống kê nhanh telemetry, phân loại hạng VIP, kiểm soát trạng thái khóa/mở tài khoản.
- **Nhật ký kiểm toán (`/admin/audit-logs`)**: Ghi vết bảo mật và hoạt động điều hành hệ thống với thanh tỷ lệ phân bổ mini.

---

## 🏗️ Kiến Trúc Hệ Thống

```
WebXemPhim/
├── src/
│   ├── Server/
│   │   └── WebXemPhim.Server/     # ASP.NET Core REST Web API, EF Core, Authentication & Security
│   ├── Client/
│   │   └── WebXemPhim.Client/     # Blazor Web App (Interactive Server Components), Scoped State
│   └── Shared/
│       └── WebXemPhim.Shared/     # DTOs, Enums, Models & Tiện ích bảo mật dùng chung
├── database/
│   ├── postgresql/                # Script khởi tạo schema, hàm chuẩn hoá & FTS PostgreSQL 17
│   └── mysql/                     # Scripts khởi tạo cấu trúc CSDL và dữ liệu mẫu MySQL (tùy chọn)
├── docs/                          # Tài liệu kiến trúc bảo mật, deployment, PostgreSQL & stream delivery
├── tests/
│   └── WebXemPhim.Tests/          # Bộ kiểm thử tự động toàn diện (1312 tests PASS 100%)
└── WebXemPhim.slnx                # Solution file (.NET 10)
```

---

## 🚀 Hướng Dẫn Khởi Chạy

### Yêu cầu tiên quyết
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- **PostgreSQL 17** (khuyến nghị chạy nhanh qua Docker Compose: `docker compose up -d postgres`)

### 1. Khởi chạy Server (Web API)
Nếu mới clone dự án lần đầu, sao chép file cấu hình mẫu cho môi trường Development:
```powershell
copy src\Server\WebXemPhim.Server\appsettings.Development.example.json src\Server\WebXemPhim.Server\appsettings.Development.json
copy src\Client\WebXemPhim.Client\appsettings.Development.example.json src\Client\WebXemPhim.Client\appsettings.Development.json
```
Khởi chạy Server:
```bash
dotnet run --project src/Server/WebXemPhim.Server/WebXemPhim.Server.csproj --launch-profile http
```
- API Endpoint: `http://localhost:5085`
- Tài liệu OpenAPI: `http://localhost:5085/openapi/v1.json`
- Mặc định môi trường Development sử dụng PostgreSQL (`localhost:5432`, database `webxemphim`) và tự động nạp dữ liệu mẫu.

### 2. Khởi chạy Client (Blazor App)
```bash
dotnet run --project src/Client/WebXemPhim.Client/WebXemPhim.Client.csproj --launch-profile http
```
- Truy cập trình duyệt: `http://localhost:5229`

*(Trên Windows có thể chạy nhanh cả Server và Client bằng script `run-app.bat` và dừng bằng `stop-app.bat`).*

---

## 🧪 Kiểm Thử Tự Động (Unit & Integration Tests)

Dự án sở hữu bộ test tự động kiểm thử toàn diện cả Server API, Controller, Service logic, PostgreSQL Full-Text Search, Stream Security và Client:
```bash
dotnet test WebXemPhim.slnx
```
*Trạng thái: 1312 / 1312 Testcases PASS 100% (0 Failed).*

---

## 🐳 Triển Khai Docker & CI/CD Lên Môi Trường Internet

Dự án hỗ trợ sẵn sàng triển khai bằng Docker Compose kèm Caddy (tự động cấp chứng chỉ SSL Let's Encrypt / HTTPS) và quy trình CI/CD tự động từ GitHub Actions:

- **Triển khai Production lên VPS qua GitHub Actions**: Xem hướng dẫn tại [docs/vps-deployment-guide.md](docs/vps-deployment-guide.md).
- **Triển khai Container Cục bộ (Local Docker)**: Xem chi tiết tại [DEPLOY_DOCKER.md](DEPLOY_DOCKER.md).

---

## 🔑 Tài Khoản Thử Nghiệm (Development Seed)

| Phân quyền | Email / Tên đăng nhập | Mật khẩu |
| :--- | :--- | :--- |
| **Quản trị viên (Admin)** | `admin@cinemaflow.vn` (hoặc gõ `admin`) | `Password123!` |
| **Hội viên VIP** | `vip@cinemaflow.vn` (hoặc `hoangnam@cinemaflow.vn`) | `Password123!` |
| **Người dùng thông thường** | `thuylinh@cinemaflow.vn` (hoặc `longle@cinemaflow.vn`) | `Password123!` |

---

## 📄 Bản Quyền
Dự án được phát triển và vận hành tại [kynhungvanlaky-max/w-xemphim](https://github.com/kynhungvanlaky-max/w-xemphim).
