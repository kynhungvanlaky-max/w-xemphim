# Cấu hình triển khai bảo mật

Các giá trị dưới đây là tên cấu hình, không chứa secret thật. ASP.NET Core tự đọc biến môi trường có dấu `__`; ứng dụng **không tự đọc file .env**.

| Cấu hình | Client | Server |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` | `Production` |
| `AllowedHosts` | Tên miền client, phân tách bằng `;`, không wildcard | Tên miền API, không wildcard |
| `Security__DataProtectionKeyPath` | Đường dẫn tuyệt đối ngoài webroot, thư mục riêng có ACL | Thư mục riêng của API có ACL |
| `ApiBaseUrl` | URL API HTTPS | Không dùng |
| `ConnectionStrings__Default` | Không chứa credential DB | Connection string từ secret manager |
| `Database__SeedDemoData` | `false` | `false` |
| `Security__AllowDemoPayments` | `false` | `false` |
| `Security__AllowedOrigins__0` | Origin client HTTPS nếu reverse proxy thay đổi origin nội bộ | Chỉ origin client HTTPS; để trống nếu không gọi API từ browser |
| `Security__Smtp__Host`, `Port`, `From` | Không dùng | Máy chủ SMTP TLS, mặc định port 587 |
| `Security__Smtp__Username`, `Password` | Không dùng | Secret SMTP từ môi trường/secret manager |
| `Security__PasswordResetClientUrl` | Không dùng | Origin client HTTPS, không query/path/userinfo |
| `Security__RateLimits__ApiPerMinute` | Không dùng | Mặc định 300 cho mỗi IP kết nối |
| `Security__RateLimits__AuthPerMinute` | Không dùng | Mặc định 15 cho mỗi IP kết nối, cho register/login/forgot/reset |

## Trước khi public

- HTTPS phải được Kestrel xử lý trực tiếp hoặc reverse proxy tin cậy được cấu hình đúng. Bản này không tự tin X-Forwarded-*; nếu cần proxy, cấu hình danh sách IP proxy và Forwarded Headers trước HTTPS/Origin/rate middleware. HTTP không được xử lý nội dung production ngay cả khi redirect chưa biết port.
- Blazor Server gọi API từ server, nên nhiều user dùng chung IP API: cân chỉnh hạn mức dựa trên tải thực. Chặn brute force theo account (5 lần sai khóa 15 phút) vẫn độc lập với IP. Multi-instance cần hạn mức phân tán và bảo vệ ở gateway; tránh tin IP do caller tự khai.
- Đừng mang DB Development sang production. Nếu đã dùng DB demo, đổi mật khẩu/xóa tài khoản demo bằng quy trình vận hành tin cậy trước khi public; không thể xem mật khẩu mẫu là secret.
- Tạo user quản trị bằng luồng đăng ký bình thường, xác minh danh tính qua quy trình vận hành rồi cấp `user_security.IsAdmin` theo đúng `UserId`. Không có API tự cấp role từ email hay từ request của member.
- Quyền DB chạy ứng dụng tối thiểu; schema/bootstrap sử dụng tài khoản vận hành riêng theo quy trình deploy. Chạy backup/restore kiểm chứng và kiểm tra bảng `user_security` đã có trước khi hạ quyền DDL.
- Mã hóa Data Protection keys at rest bằng certificate/DPAPI/secret service phù hợp host. Secret TOTP được Data Protection bảo vệ, vì vậy mất key sẽ khiến MFA không thể xác minh; cấu hình hiện tại buộc thư mục riêng production nhưng **chưa tự chọn certificate hoặc cơ chế mã hóa key at rest**.
- Cổng thanh toán thật chưa tích hợp. Đơn Pending không cấp VIP. Không sửa trạng thái từ trình duyệt; phải xây xác nhận server-to-server có chữ ký, đối chiếu giao dịch và idempotency trước khi nhận tiền thật.
- SMTP chưa cấu hình: forgot-password trả 503 cho mọi email, không tạo/tiết lộ token. Khi cấu hình, token băm nằm trong DB, token gốc chỉ được gửi tới email; cần theo dõi giao email và bổ sung queue để giảm khác biệt thời gian phản hồi.
- CSP dùng nonce cho ImportMap và không cho script inline tùy ý. Style inline được giữ cho các component đang dùng style động; media/image HTTPS bên ngoài còn được cho phép để giữ catalog, nên hãy thu hẹp host CDN khi xác định nguồn thực tế.
- Kiểm tra logout và re-login sau deploy. Token plaintext cũ bị xóa/thu hồi; việc này không xóa tài khoản/thư viện/đánh giá, không cần và không nên khôi phục các token đó.

## Kiểm tra định kỳ

Chạy `dotnet test`, `dotnet build WebXemPhim.slnx` và `dotnet list WebXemPhim.slnx package --vulnerable --include-transitive`. Audit sạch chỉ nói chưa có lỗ hổng dependency được NuGet biết tại thời điểm kiểm tra, không phải chứng nhận website an toàn tuyệt đối.

Hạng mục tiếp theo: chính sách buộc mọi admin hoàn tất đăng ký MFA trước khi cấp quyền vận hành, xác minh email, mật khẩu rò rỉ, dashboard cảnh báo, kiểm thử xâm nhập trên staging và phân quyền nguồn phim VIP/signed media URL. Audit log đã được lưu trong DB nhưng cần quy định retention, xuất log bất biến và cảnh báo tập trung.
