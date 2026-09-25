# WebXemPhim - Developer & Agent Instructions

## BẮT BUỘC TUÂN THỦ CÁC QUY TẮC SAU:

1. **TESTING FIRST KHI SỬA CODE**:
   - Khi sửa đổi hoặc bổ sung code logic, PHẢI chạy `dotnet test`.
   - **Tất cả testcases phải PASS 100%**. Không được để sót bất kỳ testcase nào bị fail.

2. **BUILD CHECK TOÀN BỘ PROJECT TRƯỚC KHI KẾT THÚC ĐOẠN CHAT (XỬ LÝ TRIỆT ĐỂ CHO TỚI KHI HẾT LỖI)**:
   - Mỗi lần làm xong công việc hoặc trước khi kết thúc bất kỳ lượt phản hồi (turn) nào, PHẢI tiến hành chạy lệnh build lại toàn bộ solution (`dotnet build WebXemPhim.slnx`).
   - Nếu phát sinh bất kỳ lỗi nào (bao gồm lỗi biên dịch C#, lỗi Razor component, lỗi liên kết hay lỗi tranh chấp file lock `MSB3021/MSB3027` do process đang chạy ngầm), PHẢI tiến hành phân tích, giải phóng lock hoặc sửa code liên tục cho tới khi **BUILD SUCCEEDED 100% (0 Error)** mới thôi. Tuyệt đối không được kết thúc đoạn chat khi build còn lỗi.

3. **KIẾN TRÚC TÁCH BIỆT RÕ RÀNG SERVER & CLIENT (C# ONLY)**:
   - **Server**: ASP.NET Core Web API (REST API viết bằng C#).
   - **Client**: Blazor Web App (C#) gọi REST API từ Server qua `HttpClient`.
   - **Shared**: Thư viện dùng chung DTOs, Enums, Models giữa Client và Server.
   - **Tests**: Thư mục `tests/` với các unit test kiểm thử toàn diện.

4. **THIẾT KẾ UI/UX CHUẨN TASTE-SKILL**:
   - Tuân thủ kỹ năng `taste-skill` (`.agents/skills/design-taste-frontend/SKILL.md`).
   - Khai báo One-line Design Read & 3 Dials (`DESIGN_VARIANCE: 7`, `MOTION_INTENSITY: 6`, `VISUAL_DENSITY: 5`).
   - Thiết kế chuẩn điện ảnh (Cinema-grade dark theme, Obsidian palette, Accent Crimson/Amber, Skeleton loading, không dùng AI slop purple gradients hay spinner tròn lỗi thời).

5. **ĐỒNG BỘ CĂN LỀ CONTAINER (CONTAINER ALIGNMENT DISCIPLINE)**:
   - Tất cả các phân vùng nội dung trên cùng trang (như trang chủ, trang khám phá, trang chi tiết phim) PHẢI nằm trọn vẹn trong một khung bọc container duy nhất (ví dụ: `.home-expanded` hoặc container chuẩn).
   - Tuyệt đối KHÔNG ngắt đôi thẻ bọc container để chèn các section tự do ở giữa, gây lệch lề hoặc vỡ layout lưới.
   - Các section con (`.genre-bento-section`, `.movie-section`) phải đặt `width: 100%`, `margin-inline: 0`, `padding-inline: 0` để các thẻ và tiêu đề căn thẳng hàng tuyệt đối (100% pixel-perfect) với mép lề của các poster/card bên cạnh.
   - Thống nhất cấu trúc tiêu đề phân vùng (sử dụng class chuẩn `.home-section-heading` thay vì tự tạo class tiêu đề riêng lẻ gây sai lệch font-size/khoảng cách).

6. **THANH CÔNG CỤ & TIỆN ÍCH TỐI GIẢN (MINIMALIST UTILITY TOOLBAR)**:
   - Các thanh điều khiển phụ trợ (như thanh chọn nguồn phát phim, thanh công cụ video, filter bar) phải thiết kế siêu gọn nhẹ (chiều cao thanh <= 38px, chiều cao nút <= 28px).
   - Tối giản tối đa nhãn phím: Ưu tiên nhãn ngắn gọn (`Nguồn 1  18 ms`, `Nguồn 2  24 ms`) thay vì văn bản mô tả dài dòng, phô trương thông số gây chiếm dụng diện tích.
   - Đưa các thông tin chi tiết (đặc tả máy chủ, phân giải 4K, CDN, mô tả) vào thuộc tính `title` (native tooltip) khi người dùng rê chuột.
   - TUYỆT ĐỐI KHÔNG sử dụng hiệu ứng viền neon lòe loẹt, chấm màu đa sắc cạnh tranh hay đổ bóng nặng (`box-shadow: none` hoặc mờ phẳng tinh tế).

7. **TƯƠNG PHẢN ĐỒNG THỜI LIGHT MODE & DARK MODE (DUAL-THEME CONTRAST & VISIBILITY)**:
   - Khi chỉnh sửa hoặc bổ sung bất kỳ thành phần giao diện nào (nút gạt toggle switch, thanh lọc, thẻ card, thanh công cụ, icon, nhãn telemetry), PHẢI định nghĩa và kiểm tra độ tương phản trên cả 2 chế độ:
     + Dark Mode: Nền Obsidian `#07080a` / `#12141a`, viền mờ tinh tế `rgba(255,255,255,0.08)`.
     + Light Mode: Nền `#ffffff` / `#f8fafc`, viền định hình dập nổi `#cbd5e1` / `#e2e8f0`, màu chữ tương phản cao `#0f172a` / `#475569`, không bao giờ để xảy ra tình trạng chữ trắng/xám mờ trên nền sáng hoặc viền bị chìm mất khối.
     + Các công tắc xúc giác (Tactile switches): Trạng thái bật có hào quang màu rõ rệt, trạng thái tắt phải có viền và rãnh tương phản rõ ràng trên nền trắng.
