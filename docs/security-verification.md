# Kết quả kiểm chứng bảo mật

Ngày kiểm tra: 2026-09-19. Phạm vi là mã nguồn và môi trường local, không phải pentest hay chứng nhận production.

## Kết quả

- `dotnet build WebXemPhim.slnx`: thành công, 0 warning, 0 error.
- `dotnet test WebXemPhim.slnx`: 388/388 PASS, 0 fail, 0 skip; bao gồm toàn bộ test cũ và các kiểm thử bảo mật mới.
- Đã làm mới dữ liệu bằng `dotnet restore WebXemPhim.slnx --force-evaluate`, sau đó chạy `dotnet list WebXemPhim.slnx package --vulnerable --include-transitive --no-restore`: cả 4 project không báo dependency có lỗ hổng đã biết. Kết quả chỉ có ý nghĩa tại thời điểm kiểm tra.
- Trình duyệt local: guest mở trực tiếp `/admin` bị chuyển tới đăng nhập; tài khoản VIP thường thử quay lại `/admin` bị chuyển về `/account`; cả hai trạng thái đều không render sidebar, KPI hay nội dung vận hành. Đăng xuất trở về trạng thái guest.
- Trang quên mật khẩu khi chưa cấu hình SMTP hiển thị “Khôi phục mật khẩu chưa khả dụng. Vui lòng liên hệ hỗ trợ.”, không hiển thị token/link reset cho caller.

## Những kiểm thử âm tính đã bổ sung

- Guest/member bị từ chối API quản trị; email giống admin và field role tự gửi không cấp quyền. Quyền admin do DB quyết định và thu hồi có hiệu lực ở request sau.
- Guest/member không nhận markup giao diện admin khi truy cập trực tiếp `/admin`, `/admin/users` hoặc `/admin/movies`; layout chỉ render nội dung sau khi trạng thái xác thực đã khởi tạo và xác nhận role Admin.
- Mật khẩu bypass bị từ chối, hash cũ chỉ nâng cấp sau mật khẩu hợp lệ; 5 lần đăng nhập sai khóa tạm tài khoản.
- DB không lưu access/reset token gốc; token plaintext cũ không được chấp nhận. Logout thu hồi đúng session; reset token dùng một lần và thu hồi các session/reset khác.
- Watchlist thuộc từng user; alias cũ không còn mở thư viện dùng chung công khai.
- Checkout không mô phỏng trả Pending, không cấp VIP hay đánh dấu đã trả tiền.
- Origin lạ không được CORS cho phép; Blazor từ chối Origin lạ; giới hạn request trả 429 và Retry-After.
- URL quản trị không an toàn/dữ liệu quá dài bị từ chối; header no-store, nosniff, anti-clickjacking và CSP nonce được kiểm tra.
- Client production từ chối khởi động khi thiếu cấu hình key bắt buộc; cấu hình demo, wildcard origin và key trong webroot bị từ chối.
- MFA TOTP dùng cửa sổ thời gian chuẩn; secret trong DB không phải khóa thủ công dạng rõ, recovery code không lưu dạng rõ, đăng nhập chỉ cấp session sau lớp thứ hai. Session admin cũ chưa qua MFA mất role Admin sau khi tài khoản bật MFA.

## Giới hạn và bàn giao

Chưa cấu hình hoặc kiểm thử TLS/reverse proxy/SMTP thật, webhook thanh toán thật, WAF, backup restore, multi-instance hay phân quyền signed media VIP. MFA TOTP đã triển khai và kiểm thử local; cần bảo vệ/backup Data Protection keys production. Không gửi email hay thu tiền thật trong quá trình kiểm tra. Hãy hoàn thành [checklist triển khai](security-deployment.md) trước khi public website.

Phiên đăng nhập dùng token plaintext cũ đã bị thu hồi. Người dùng đăng nhập lại; dữ liệu tài khoản/phim/thư viện/đánh giá được giữ.
