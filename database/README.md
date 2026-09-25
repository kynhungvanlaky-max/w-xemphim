# Database

Ứng dụng hiện dùng MySQL 8 thông qua EF Core. Toàn bộ model và service chỉ phụ thuộc
`AppDbContext`; cấu hình provider được gom tại `DatabaseRegistration.cs` để việc đổi
sang PostgreSQL sau này không làm thay đổi controller hoặc business service.

## Khởi tạo MySQL

1. Tạo user MySQL phù hợp với môi trường.
2. Chạy lại `database/mysql/001_initial.sql` để tạo hoặc bổ sung schema. Script dùng
   `IF NOT EXISTS`, nên các bảng catalog mới không xóa dữ liệu tài khoản hiện có.
3. Ghi đè connection string bằng biến môi trường, không commit mật khẩu production:

```powershell
$env:ConnectionStrings__Default="Server=localhost;Port=3306;Database=webxemphim;User=webxemphim;Password=your_password;"
dotnet run --project src/Server/WebXemPhim.Server
```

Trong môi trường Development, server tự bổ sung dữ liệu còn thiếu:

- 36 phim cùng thể loại, diễn viên, tập phim và các nguồn phát;
- 18 tài khoản khán giả;
- 216 đánh giá phân bổ trên catalog;
- 6 giao dịch VIP mẫu.

Tài khoản chính ở DB Development mới: `demo@cinemaflow.vn` / `Password123!`. Mật khẩu tài khoản đã có không bị seeder ghi đè. Các tài khoản từ
`viewer02@cinemaflow.demo` đến `viewer18@cinemaflow.demo` dùng mật khẩu
`Cinema123`. Seeder chạy lặp lại an toàn và không nhân bản dữ liệu.

File `002_seed_data.sql` là bộ dữ liệu kiểm thử cũ có thao tác `TRUNCATE`. Chỉ dùng
khi cần reset riêng tài khoản, token, thanh toán và đánh giá; chạy sau khi catalog đã
được seed. Có thể tắt automatic seeding bằng `Database__SeedDemoData=false`.

Không chạy bất kỳ demo seeder nào trên production. Xem [kế hoạch và cấu hình bảo mật](../docs/security-plan.md). Token cũ/predictable sẽ bị thu hồi khi khởi động phiên bản đã harden; user đăng nhập lại qua API. Quyền admin là cờ server trong bảng `user_security`, không được suy từ email/VIP. Schema helper tạo bảng bổ sung khi khởi động; thao tác production cần backup và tài khoản quản trị DB riêng trước khi cấp quyền.

## Chuyển sang PostgreSQL sau này

Thay package `MySql.EntityFrameworkCore` bằng `Npgsql.EntityFrameworkCore.PostgreSQL`,
đổi duy nhất `UseMySQL(...)` thành `UseNpgsql(...)` trong `DatabaseRegistration.cs`,
rồi tạo migration/schema PostgreSQL. Entity, service và API không cần đổi.

Test integration dùng EF Core InMemory với database riêng cho mỗi test factory, nên
không đọc hoặc ghi vào database MySQL local.
