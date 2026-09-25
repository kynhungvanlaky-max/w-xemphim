# Kiểm toán toàn bộ UI điện thoại và popup — 25/09/2026

## Kết luận

Giao diện điện thoại hiện **chưa đạt để phát hành**. Có lỗi cản trở thao tác: popup lưu phim và tìm nhanh quản trị vượt khung nhìn, ô tìm nhật ký chỉ còn rộng khoảng 7 px, bộ chọn phim quản trị bị cắt, các nút chính của flow đăng nhập/đăng ký/khôi phục nằm rất sâu dưới màn đầu. Ảnh người dùng gửi thể hiện chính xác khe hở phía trên khay tìm kiếm: header cao 60 px nhưng khay bắt đầu ở y=68.

Báo cáo này ghi rõ **lỗi đã đo**, **vấn đề UX đã quan sát**, và **trạng thái chưa được xác minh**. Một lượt sửa mã nguồn đã được thực hiện sau kiểm toán; trạng thái nghiệm thu được ghi ở cuối tài liệu.

## Phạm vi và cách kiểm tra

- Mở ứng dụng local trong trình duyệt ở 281 × 479, 320 × 479, 320 × 568/700 và 390 × 844; xem ảnh màn hình, đo DOM/khung phần tử, đối chiếu mã CSS/Razor. Kiểm tra khách, thành viên và tài khoản demo admin; soi dark/light ở các vùng trọng yếu.
- Trực tiếp mở: tìm kiếm/menu/notification/tài khoản, popup lưu phim, chọn VIP, thêm phim, tạo tập hàng loạt, command palette, popup báo lỗi ở 320 × 700. Không gửi biểu mẫu, tạo thanh toán hay xóa dữ liệu.
- Chưa kiểm tra đủ mọi trạng thái dữ liệu, màn hình xoay, zoom 200%, bàn phím ảo thực và Safe Area iOS. Một lần điều hướng local sau cùng bị chính sách URL của trình duyệt chặn; đã dừng thao tác đó. Những popup chưa mở được ghi riêng, không suy diễn thành lỗi đã xác nhận.
- Bảng admin rộng có vùng cuộn ngang riêng; ở 320 px không ghi nhận chúng làm tràn toàn bộ trang.

## Lỗi đã xác nhận

| ID | Mức | Vị trí / điều kiện | Bằng chứng và tác động | Hướng sửa |
| --- | --- | --- | --- | --- |
| M01 | P1 | Header khách, 320 px | Nút Đăng nhập x≈216–304 chồng vùng nút menu x≈270–308; trạng thái đăng nhập bị chen. | Header ≤360 px dùng nút tài khoản gọn hoặc đưa đăng nhập vào menu; mọi vùng chạm riêng ≥44 px. Xem `NavMenu.razor`, `NavMenu.razor.css`, `popup-polish.css`. |
| M02 | P1 | Mở tìm kiếm mobile, 281 × 479; lỗi trong ảnh gửi | Header kết thúc y=60, drawer bắt đầu y=68: lộ khe **8 px**, nội dung trang lọt qua. | Dùng chung biến chiều cao header, neo drawer/backdrop ngay dưới header. `NavMenu.razor.css:500–503` đặt `top:68px`. |
| M03 | P1 | `/login`, 320 × 568 | Nút Đăng nhập ở y≈1052–1097; phải cuộn ≈500 px mới thấy. 390 × 844 vẫn y≈968. | Thu hero và đưa form/CTA lên màn đầu; rút thẻ demo. `cinema-theme.css:261,707`, `redesign-2026.css:944,1024`. |
| M04 | P1 | `/register`, 320 × 568 | Tạo tài khoản ở y≈1155–1200; tại 390 × 844 vẫn y≈1097. | Dùng layout xác thực ưu tiên form trên mobile; kiểm tra bàn phím. |
| M05 | P1 | `/forgot-password`, `/reset-password`, 320 × 568 | Gửi liên kết ở y≈1129–1174; Lưu mật khẩu mới y≈996–1041. Tác vụ ngắn lại bị đặt sâu sau hero. | Thu/ẩn hero mobile trên flow phục hồi, đưa trường và CTA lên trước. |
| M06 | P1 | `/movie/1`, khách bấm Thêm vào tủ, 320 × 568 | Popup cao ≈608 px, từ y≈−20 tới 588; đầu/cuối vượt màn hình, nút xác nhận gói thành nhiều dòng. | Giới hạn `max-height:calc(100dvh - 2rem)`, cho body cuộn, header/footer khả dụng, giảm poster/copy. `MovieDetail.razor:288–326`, `MovieDetail.razor.css:741–761` đang căn giữa và `overflow:hidden`. |
| M07 | P1 | `/vip`, khách chọn gói, 320 × 479 | Popup ở y≈76–467 nhưng nội dung scrollHeight≈418 > clientHeight≈389, overflow-y:hidden; nút footer tới y≈475 nên bị cắt. Ở 320 × 568 popup vừa. | Giới hạn bằng `100dvh`, body cuộn, footer trong card; thử bàn phím và Safe Area. `Vip.razor.css:1247`, `popup-polish.css:542–555`. |
| M08 | P1 | `/admin/audit-logs`, Tìm nhanh, 320 × 479 | Command palette y≈57, cao≈495, đáy≈552: **73 px ngoài màn hình**. | Giảm `padding-top:12vh` trên máy thấp, đặt max-height theo `100dvh`, danh sách co/cuộn. `AdminCommandPalette.razor`, `admin-workspace.css:1910–1973`. |
| M09 | P1 | `/admin/audit-logs`, 320 × 568 | Ô tìm “Tìm theo quản trị viên…” chỉ rộng **≈7 px**, thực tế không nhập được; chọn tự làm mới chiếm cùng hàng. | Search chiếm hàng riêng và toàn chiều rộng, controls xuống hàng; `min-width:0` cho flex child. `AdminAuditLogs.razor:36–61`, `admin-workspace.css:286–319`. |
| M10 | P1 | `/admin/episodes`, 320 px | Select Đang chọn tác phẩm rộng ≈457 px, vượt card; tiêu đề Quản lý tập phim thành cột hẹp. | Bỏ `min-width:320px` inline tại `AdminEpisodes.razor:61`; xếp tiêu đề/select dọc, select `width:100%;min-width:0`. |
| M11 | P2 | `/admin/movies`, `/admin/episodes`, `/admin/users`, 320 × 568 | Tab rộng 54–83 px và cao lần lượt ≈114/94/134 px; nhãn xuống 3–4 dòng. | Dải tab cuộn ngang một hàng, `white-space:nowrap`, cao ≤38 px, có dấu hiệu cuộn. `admin-workspace.css:552–588`. |
| M12 | P2 | `/watch/1`, 320 × 700 | Thanh 4 nguồn thành hai hàng cộng hàng tiện ích, tổng cao ≈140 px, vượt chuẩn toolbar ≤38 px của AGENTS.md. | Chọn nguồn bằng menu một hàng, nhãn ngắn, thông tin đầy đủ trong `title`; nút ≤28 px. `Watch.razor.css:317–325`. |
| M13 | P2 | `/profile/settings`, light mode, 320 px | Chữ nhỏ trên banner ảnh tối gần như chìm vào nền. | Gom override light và thêm lớp phủ/nền chắc chắn cho chữ; đo tương phản. `profile-settings.css:23–76`, `light-theme.css`. |
| M14 | P2 | `/account` và `/profile`, 320 × 568 | Email/trạng thái nhỏ trên hero ảnh có tương phản yếu. | Tăng shade dưới vùng chữ hoặc đặt trên nền đồng nhất; thử tên/email dài. `account-overview.css:35–54`. |
| M15 | P2 | Rail tài khoản, 320 px | Chỉ thấy Tổng quan và Hồ sơ & cài đặt trong màn đầu; các mục khác nằm ngoài ngang, thiếu dấu hiệu cuộn. | Cho thanh tab cuộn ngang có cue/indicator hoặc menu ngắn; tab hiện tại tự cuộn vào vùng nhìn. `redesign-2026.css:920–929`. |
| M16 | P2 | Popup tạo tập hàng loạt, 320 × 700 | Nút Khởi tạo hàng loạt gói thành 3 dòng. | Footer mobile xếp dọc hoặc rút nhãn; thử lại 320 × 479. `AdminEpisodes.razor`, `.admin-modal-footer`. |
| M17 | P3 | Mọi route ở viewport 281 px | `documentElement.scrollWidth=320`: 39 px bị cắt. Ảnh gửi có bề rộng 281 px nhưng không khẳng định viewport thiết bị thật là 281 CSS px. | Nếu hỗ trợ <320 px, bỏ `body{min-width:320px;overflow-x:hidden}` ở `cinema-theme.css:47` và sửa từng thành phần; nếu ngưỡng sản phẩm là 320 px, ghi rõ. |

P1 = mất/khó thực hiện hành động hoặc popup bị cắt; P2 = khó đọc, khó tìm hoặc chiếm diện tích quá mức; P3 = giới hạn thiết bị. Các tọa độ là phép đo trong một trạng thái, cần xác nhận thêm trên thiết bị thật.

## Ma trận từng trang

| Trang | Đã quan sát | Kết luận hiện tại / việc phải chốt |
| --- | --- | --- |
| `/` | Màn đầu, khay tìm, khách | M01–M02. CTA Xem ngay y≈497 tại 320 × 568, vẫn thấy nhưng sát đáy. Kiểm tra carousel, tên dài và skeleton. |
| `/explore` | Màn đầu | Không thấy tràn toàn trang ở 320. Mở mọi filter, nhiều chip, kết quả rỗng, bàn phím tìm kiếm. |
| `/genres` | Màn đầu | Tiêu đề y≈193, tìm kiếm y≈400, danh sách dưới fold. Kiểm tra từng thể loại và ảnh/tên dài. |
| `/random` | Màn đầu | Tiêu đề y≈192, filter y≈400; hành động chọn phim dưới fold, cần ưu tiên rõ hơn. Kiểm tra kết quả/rỗng. |
| `/movie/{Id}` | Phim mẫu, khách | M06; CTA Xem phim y≈655 tại 320 × 568. Kiểm tra review, tập, liên quan, phim tên dài. |
| `/watch/{MovieId}` | Phim mẫu, toolbar | M12. Kiểm tra chọn tập/chất lượng, fullscreen, player lỗi và báo lỗi ở màn thấp. |
| `/vip` | Khách, chọn gói | M07. Kiểm tra luồng đã đăng nhập, thanh toán thành công/thất bại, mọi gói. |
| `/login` | Khách | M03. Kiểm tra sai mật khẩu, gửi, link, bàn phím. |
| `/register` | Khách | M04. Kiểm tra validation, checkbox, lỗi server, bàn phím. |
| `/forgot-password` | Khách | M05. Kiểm tra gửi/thành công/lỗi. |
| `/reset-password` | Không có token hợp lệ | M05. Kiểm tra token hợp lệ/hết hạn và validation. |
| `/not-found` | Màn đầu | CTA về trang chủ y≈844 tại 320 × 568; dưới fold, mức ưu tiên thấp. |
| `/Error` | Chỉ kiểm tra route trong mã | **Chưa quan sát trực tiếp**; tạo lỗi có kiểm soát, kiểm tra thông báo/CTA. |
| `/account`, `/profile` | Thành viên, màn đầu | M14–M15. Kiểm tra chỉ số, VIP, tên/email dài và light mode. |
| `/profile/settings` | Thành viên | M13, M15. Kiểm tra từng form, avatar, validation, toast, bàn phím. |
| `/history` | Thành viên, màn đầu | M15; chưa thấy chồng lấn khác. Kiểm tra dài/rỗng và xác nhận xóa. |
| `/watchlist` | Thành viên, màn đầu | Chưa thấy tràn/chồng lấn; kiểm tra dài/rỗng, lọc/xóa, poster lỗi. |
| `/notifications` | Thành viên, màn đầu | M15; chip lọc xuống hàng nhưng đọc được. Kiểm tra nhiều/chưa đọc/xóa. |
| `/admin`, `/admin/overview` | Admin, màn đầu | Chưa thấy lỗi cản trở; kiểm tra biểu đồ và nội dung dưới fold. |
| `/admin/movies` | Admin, danh sách và thêm phim | M11. Popup thêm phim tại 320 × 479 y≈68–459, nằm trong màn và body cuộn. Kiểm tra edit/clone/inspector/xóa. |
| `/admin/episodes`, `/admin/episodes/{MovieId}` | Admin, danh sách | M10–M11, M16. Kiểm tra tạo/sửa, batch, probe, stream và URL có MovieId. |
| `/admin/users` | Admin, màn đầu | M11. Kiểm tra drawer hồ sơ, khóa/mở, VIP và email dài. |
| `/admin/reviews` | Admin, màn đầu | Chip lọc xuống hàng nhưng đọc được. Kiểm tra trả lời, ẩn/xóa, comment dài. |
| `/admin/vip` | Admin, màn đầu | Dải tab cuộn ngang có scrollbar; chưa kết luận lỗi. Kiểm tra duyệt/từ chối, chi tiết, sửa gói. |
| `/admin/audit-logs` | Admin, màn đầu và tìm nhanh | M08–M09. Kiểm tra drawer log, JSON dài, filter và phân trang. |

## Danh mục popup, menu và drawer

| Thành phần | Mức kiểm tra | Kết luận / tiêu chí |
| --- | --- | --- |
| Tìm kiếm mobile | Đã mở | M02. Mép trên nối header; kết quả/bàn phím không che đóng. |
| Menu mobile, thông báo, menu tài khoản | Đã mở ở 320 × 700 | Chưa thấy vỡ tại kích thước này; thử 320 × 479, list dài, light mode. |
| Yêu cầu đăng nhập để lưu phim | Đã mở | M06; đầu, cuối, nút đóng và CTA phải tiếp cận được. |
| Yêu cầu đăng nhập khi chọn VIP | Đã mở | M07; kiểm tra màn thấp và bàn phím. |
| Báo lỗi trình phát | Đã mở ở 320 × 700, chưa kiểm tra 320 × 479 | **Rủi ro**, chưa xác nhận lỗi: `Watch.razor.css:272` cho `overflow:auto` nhưng `popup-polish.css:367` ghi `overflow:hidden!important`. Kiểm tra textarea và nút gửi khi viewport thấp/bàn phím. |
| Menu tập/chất lượng trong player | Chưa mở đủ | Không vượt viewport; đóng bằng Escape/backdrop, focus đúng. |
| Command palette admin | Đã mở | M08; chiều cao co theo viewport, kết quả cuộn. |
| Thêm/sửa phim | Đã mở thêm phim | Card thêm phim vừa 320 × 479; kiểm tra toàn form, validation và bàn phím. |
| Nhân bản, inspector, xác nhận xóa phim | Chưa mở đủ | Kiểm tra drawer/confirm; không thực hiện thay đổi dữ liệu trong audit. |
| Tạo/sửa tập, batch, probe, nguồn stream | Đã mở batch ở 320 × 700 | M16; kiểm tra phần còn lại ở 320 × 479. |
| Drawer người dùng; phản hồi đánh giá; VIP duyệt/từ chối/chi tiết/chỉnh gói; chi tiết audit | Chưa mở đủ | Mỗi popup giới hạn `100dvh`, body cuộn, footer/nút đóng rõ, tương phản hai theme, CTA không bị bàn phím che. |

## Kế hoạch khắc phục và tiêu chí nghiệm thu

1. **P1, popup và thanh đầu:** sửa M01–M02, M06–M10. Dùng một mẫu dialog responsive với max-height theo `100dvh`/Safe Area, phần thân cuộn, header/footer vẫn tới được. Bỏ kích thước cứng làm select/search bị ép.
2. **P1, xác thực:** sửa layout dùng chung cho M03–M05. Ở 320 × 568, form và nút chính phải thấy trong màn đầu hoặc sau rất ít cuộn; bàn phím mở vẫn tới được CTA. Giữ hình ảnh điện ảnh ở dạng thấp/gọn.
3. **P2, mật độ và tương phản:** sửa M11–M16. Tab một hàng có thể cuộn; thanh nguồn ≤38 px, nút ≤28 px; rail tài khoản có cue; chữ trên ảnh đủ tương phản trong dark/light.
4. **Chạy lại ma trận:** 320 × 479/568, 360 × 800, 390 × 844, 430 × 932; khách/thành viên/admin; dark/light; dữ liệu rỗng, dài và lỗi. Mở từng popup, cuộn, focus input và bàn phím, Escape/backdrop, xoay màn hình, kiểm tra z-index/focus. Không có CTA bị cắt/chồng, không có cuộn ngang toàn trang tại ngưỡng hỗ trợ.
5. **Kiểm tra kỹ thuật:** khi sửa code logic, chạy `dotnet test` tới 100% pass; sau mỗi đợt sửa chạy `dotnet build WebXemPhim.slnx` 0 lỗi. Kiểm tra desktop không bị hồi quy.

**One-line Design Read:** Cinema-grade dark UI, nội dung phim là trọng tâm; trên mobile ưu tiên thao tác ngắn, chữ đọc được và popup nằm trong khung nhìn. **3 Dials:** `DESIGN_VARIANCE: 7`, `MOTION_INTENSITY: 6`, `VISUAL_DENSITY: 5`. Giữ Obsidian, Crimson/Amber, skeleton loading và tương phản light mode theo `AGENTS.md`.

## Tiến độ triển khai

- Đã sửa mã cho M01–M17: menu khách đặt Đăng nhập/Đăng ký lên đầu; drawer tìm kiếm neo theo header; form xác thực ưu tiên nội dung; popup lưu phim/VIP/báo lỗi/admin có giới hạn theo `100dvh` và vùng cuộn; ô tìm/tab/select admin được bố trí lại; toolbar nguồn một hàng; tăng tương phản banner/rail tài khoản; bỏ sàn 320 px cho viewport nhỏ. CTA chi tiết phim và trang 404 cũng được đưa lên sớm hơn.
- Drawer chi tiết audit thiếu quy tắc định vị overlay trong CSS được bổ sung vào `mobile-ui-fixes.css`.
- Rà tiếp trên mã cho viewport dưới 320 px: bỏ `min-width:300px` của tiêu đề khám phá, giới hạn đúng kích thước ô tìm khám phá/thể loại, và giữ menu tài khoản trong mép màn hình. Thẻ tài khoản demo trên trang đăng nhập được chuyển xuống sau form cả trong DOM để thứ tự bàn phím hợp lý.
- Rà tiếp popup màn thấp: bỏ margin của card quản trị để `max-height` cộng padding backdrop không vượt `100dvh`, đặt `box-sizing:border-box` cho các card; các ô lọc quản trị có `grid-column:span 2` được xếp một cột trên điện thoại để tránh sinh cột ngầm ngoài màn hình.
- Đối chiếu CSS Blazor đã biên dịch để kiểm tra độ ưu tiên selector. Sửa chiều cao menu mobile theo đúng breakpoint header 68 px/60 px. Drawer quản trị phim/người dùng có nhãn dialog rõ, phần thân cuộn và footer xếp dọc để nút không vượt chiều ngang màn nhỏ.
- `dotnet test WebXemPhim.slnx -c Release`: 570 pass, 0 fail, 0 skip. Cấu hình Debug bị tiến trình `dotnet run` từ IDE bên ngoài giữ file, nên test/build dùng Release để kiểm tra code hiện tại mà không can thiệp phiên IDE.
- **Chưa nghiệm thu UI sau sửa trên trình duyệt/thiết bị** vì trình duyệt kiểm tra chặn điều hướng local theo chính sách URL. Cần đo lại toàn bộ ma trận trang/popup, dark/light, màn thấp, bàn phím ảo và Safe Area. Không ghi các mục trên là “đã hết lỗi” khi thiếu bước này.
