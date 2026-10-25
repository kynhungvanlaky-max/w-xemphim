# Database

Ứng dụng dùng **PostgreSQL** làm database runtime. Toàn bộ model và service chỉ phụ thuộc
`AppDbContext`; cấu hình provider được gom tại `DatabaseRegistration.cs` để việc đổi
sang MySQL sau này không làm thay đổi controller hoặc business service.

> Lịch sử: trước đây ứng dụng dùng SQLite, sau đó có nhánh MySQL. Kế hoạch chuyển đổi
> chi tiết nằm trong [docs/postgresql-migration-plan.md](../docs/postgresql-migration-plan.md).

## Khởi tạo PostgreSQL

### Cách 1 — Docker Compose (khuyến nghị)

```bash
docker compose up -d postgres
```

Service `postgres` (image `postgres:17-alpine`) được dựng kèm, có healthcheck và volume
`postgres_data`. Server trong compose tự trỏ về `Host=postgres`.

### Cách 2 — Script SQL thủ công

```bash
psql -U webxemphim -d webxemphim -f database/postgresql/001_initial.sql
```

Script tạo toàn bộ schema: bảng, chỉ mục, ràng buộc khóa ngoại, hàm chuẩn hoá tiếng Việt
và chỉ mục tìm kiếm toàn văn (`tsvector` + GIN). Script **idempotent** — chạy lại không
mất dữ liệu tài khoản hiện có.

### Cách 3 — EF Core migrations

Server tự áp dụng migration khi khởi động:

```powershell
dotnet ef database update --project src/Server/WebXemPhim.Server
```

Migration gồm `InitialCreate` (toàn bộ bảng) và `AddMovieSearchIndex` (cột `tsvector`,
hàm chuẩn hoá, trigger và chỉ mục GIN).

> Khi database đã được tạo bằng script SQL, server đánh dấu các migration là "đã áp dụng"
> (`BaselineMigrationsAsync`) nên không cố tạo lại bảng.

## Cấu hình

```powershell
$env:ConnectionStrings__Default="Host=localhost;Port=5432;Database=webxemphim;Username=webxemphim;Password=your_password;"
$env:Database__Provider="PostgreSql"
dotnet run --project src/Server/WebXemPhim.Server
```

Trong môi trường Development, server (`DatabaseSeeder`) tự bổ sung dữ liệu còn thiếu:

- 40 phim cùng thể loại, diễn viên, tập phim và các nguồn phát;
- 5 tài khoản mẫu (`admin@cinemaflow.vn`, `hoangnam@cinemaflow.vn`, `vip@cinemaflow.vn`, `thuylinh@cinemaflow.vn`, `longle@cinemaflow.vn`);
- Các đánh giá phim, thông báo, tiến độ xem và 5 giao dịch VIP mẫu.

Tài khoản mẫu ở DB Development dùng mật khẩu `Password123!` (quản trị viên: `admin@cinemaflow.vn` hoặc đăng nhập nhanh bằng tên `admin`). Mật khẩu tài khoản đã có không bị seeder ghi đè. Seeder chạy lặp lại an toàn và không nhân bản dữ liệu.

Không chạy bất kỳ demo seeder nào trên production. Xem [kế hoạch và cấu hình bảo mật](../docs/security-plan.md). Token cũ/predictable sẽ bị thu hồi khi khởi động phiên bản đã harden; user đăng nhập lại qua API. Quyền admin là cờ server trong bảng `user_security`, không được suy từ email/VIP. Schema helper tạo bảng bổ sung khi khởi động; thao tác production cần backup và tài khoản quản trị DB riêng trước khi cấp quyền.

## Tìm kiếm toàn văn

PostgreSQL dùng `tsvector` + GIN index thay cho FTS5 của SQLite:

| Khía cạnh | PostgreSQL |
|-----------|-----------|
| Cột chỉ mục | `movies."search_vector"` kiểu `tsvector` |
| Chuẩn hoá | `webxemphim_normalize()` — bỏ dấu tiếng Việt (kể cả `Đ`), hạ chữ thường |
| Đồng bộ | Trigger `trg_movies_search_vector` + `trg_cast_movies_search_vector` |
| Truy vấn | `to_tsquery('simple', ...)` + `ts_rank_cd()` (prefix match `:*`) |
| Tiện ích | `unaccent` extension (trusted extension từ PG 13; nếu thiếu quyền thì vẫn chạy, chỉ bỏ dấu ASCII + `Đ`) |

## MySQL (tùy chọn)

Nhánh MySQL vẫn được giữ lại và script DDL nằm ở `database/mysql/`. Chuyển sang MySQL bằng cách đặt
`Database__Provider=MySql` và trỏ connection string về server MySQL. Lưu ý: nhánh MySQL **không**
có chỉ mục tìm kiếm toàn văn, tìm kiếm dùng cột `SearchText` chuẩn hoá sẵn.

## Test

Test integration dùng **PostgreSQL thật** qua Testcontainers (`PostgresTestDatabase`): một
container dùng chung cho toàn bộ assembly, mỗi test class có database riêng nên không rò rỉ
dữ liệu. Chạy test cần Docker:

```bash
dotnet test
```

Phần test logic thuần .NET vẫn dùng EF Core InMemory nên không cần Docker.
