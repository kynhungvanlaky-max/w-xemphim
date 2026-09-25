# Kế hoạch bảo mật CinemaFlow

Phạm vi: ASP.NET Core API, Blazor Server, Shared DTO và kiểm thử trong repository. Không thay đổi DNS, firewall, tài khoản cloud hoặc gửi email thật khi chưa có cấu hình.

## Thứ tự triển khai

1. **P0 — khóa quyền quản trị:** API admin bắt buộc policy dựa trên quyền lưu ở server; không suy quyền từ email hoặc VIP; đăng ký không được tự cấp quyền.
2. **P0 — xác thực và khôi phục:** bỏ mật khẩu bypass; băm token trong DB; thu hồi token khi logout/reset; nâng PBKDF2-SHA256 lên 600.000 vòng, nâng cấp hash cũ khi đăng nhập thành công; khóa tạm tài khoản sau nhiều lần sai. Token reset chỉ gửi qua kênh email cấu hình, không trả về API.
3. **P0 — VIP/thanh toán:** môi trường thật chỉ tạo đơn Pending; không cấp VIP trước xác nhận từ cổng thanh toán. Thanh toán mô phỏng chỉ cho Development với cấu hình rõ ràng.
4. **P1 — bảo vệ HTTP:** CORS allowlist, giới hạn request/đăng nhập, body size, lỗi 500 không lộ stack trace, no-store cho dữ liệu cá nhân, CSP/nonce, anti-clickjacking, HTTPS/HSTS production và kiểm tra Origin cho Blazor.
5. **P1 — dữ liệu/cấu hình:** ràng buộc chiều dài/enum/URL ở API; không để credential DB trong cấu hình mẫu; production phải cấu hình host, HTTPS API và thư mục Data Protection ngoài webroot; tuyệt đối không seed demo production.
6. **Kiểm chứng:** kiểm thử âm tính cho guest/member/giả quyền, reset/logout, token DB, VIP Pending, CORS/rate limit/header/URL; chạy toàn bộ `dotnet test` và `dotnet build WebXemPhim.slnx`.
7. **P1 — MFA và audit:** TOTP tương thích ứng dụng xác thực, mã khôi phục dùng một lần, secret được Data Protection bảo vệ, challenge 5 phút/tối đa 5 lần thử. Khi tài khoản admin đã bật MFA, session chưa qua lớp thứ hai không được nhận role Admin. Sự kiện thiết lập/bật/tắt/đăng nhập MFA và logout được lưu vào audit log.

## Việc triển khai thật cần chủ website cấu hình

- Tên miền/TLS, reverse proxy tin cậy, host allowlist; không tin X-Forwarded-* từ Internet.
- Secret DB/SMTP qua secret manager hoặc biến môi trường; quyền DB tối thiểu, backup mã hóa và thử restore.
- Kênh SMTP TLS và URL client HTTPS; không có SMTP thì chức năng quên mật khẩu phải báo chưa khả dụng, không phát token cho caller.
- Cổng thanh toán thật: chữ ký webhook, đối chiếu order/amount/currency, chống replay/idempotency và transaction cấp VIP. Trước đó đơn Pending không được đổi thành Completed qua client.
- Quyền admin production cấp bằng thao tác vận hành tin cậy vào bảng quyền, không dựa tên/email. Không tự nâng tài khoản cũ thành admin ở production.
- Thư mục key có ACL riêng cho service, mã hóa key at rest bằng certificate/secret service thích hợp với host; giữ bền qua deploy và backup riêng.
- WAF/DDoS, hạn mức phân tán/Redis nếu chạy nhiều instance; log cảnh báo, giám sát và lịch cập nhật dependency. Rate limit trong ứng dụng không thay thế các lớp này.
- MFA TOTP đã có và quản trị viên được nhắc bật trong cài đặt. Xác minh email đăng ký và kiểm tra mật khẩu rò rỉ vẫn là các bước tiếp theo.

## Lưu ý chuyển đổi

Token cũ lưu plaintext sẽ không còn được chấp nhận. Người dùng cần đăng nhập lại; dữ liệu phim, thư viện, đánh giá và tài khoản được giữ nguyên. Tài khoản demo chỉ phục vụ Development và không được dùng để public website.

## Tài liệu tham chiếu

- [OWASP Authentication](https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html)
- [OWASP Forgot Password](https://cheatsheetseries.owasp.org/cheatsheets/Forgot_Password_Cheat_Sheet.html)
- [OWASP Password Storage](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html)
- [ASP.NET Core Rate Limiting](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0)
- [Blazor CSP](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/content-security-policy?view=aspnetcore-10.0)
