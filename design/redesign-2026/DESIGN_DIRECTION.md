# CinemaFlow Redesign 2026

## Design read

Rạp phim số mang chất editorial điện ảnh, tối sâu, nơi poster và film still là nhân vật chính.

- `DESIGN_VARIANCE: 7`
- `MOTION_INTENSITY: 6`
- `VISUAL_DENSITY: 5`
- Theme lock: deep dark
- Palette: Obsidian `#08090B`, Graphite `#121418`, Bone `#F2EFE8`, Crimson `#D31920`, Amber `#D8A447`
- Radius: 10px cho consumer UI, 8px cho admin UI
- Typography direction: refined grotesk cho UI, compressed display cho tiêu đề phim

## Route coverage

- `home.png`: `/`
- `home-content.png`: các section bên dưới hero của `/`
- `explore.png`: `/explore`
- `genres.png`: `/genres`
- `random.png`: `/random`
- `watchlist.png`: `/watchlist`
- `movie-detail.png`: `/movie/{id}`
- `watch.png`: `/watch/{id}`
- `account-v2.png`: `/account` (thay thế concept `account.png` có tính năng minh họa không nằm trong scope)
- `profile-settings.png`: `/profile/settings`
- `history.png`: `/history`
- `notifications.png`: `/notifications`
- `login-v2.png`: `/login` (thay thế `login.png`)
- `register-v2.png`: `/register` (thay thế `register.png`)
- `forgot-password.png`: `/forgot-password`
- `reset-password.png`: `/reset-password`
- `vip.png`: hero của `/vip`
- `vip-plans.png`: pricing và checkout của `/vip`
- `admin.png`: `/admin`
- `error.png`: `/Error`
- `not-found.png`: `/not-found`

Tổng cộng: 19 route, 21 ảnh tham chiếu được chọn, không tính các concept đã thay thế.
Ảnh là bản phác thảo desktop v1, chưa phải screenshot của website đã triển khai.
Xem ảnh riêng ở kích thước lớn trong `index.html`; không dùng một collage nén nhiều trang.

## Source assets

- `cinemaflow-hero-rice-field.png`: clean ultra-wide homepage/detail backdrop
- `cinemaflow-watch-rain-night.png`: clean player still and episode thumbnail source
- `cinemaflow-poster-rain-field.png`: clean portrait poster artwork
- `cinemaflow-projector.png`: projector artwork cho explore/account/error
- `cinemaflow-vip-corridor.png`: clean VIP hero artwork
- `cinemaflow-auth-rain.png`: clean portrait authentication artwork

Các asset mới là artwork hư cấu, không phải poster chính thức hoặc chân dung diễn viên thật.
Poster, cast, title và metadata thật vẫn lấy từ dữ liệu/API hoặc asset hiện có.
Không gán artwork hư cấu cho phim thật như thể đây là poster chính thức.

## Implementation contract

- Giữ nguyên route, API flow, dữ liệu và quyền truy cập hiện có.
- Dùng ảnh mockup trong thư mục này làm nguồn tham chiếu thị giác chính.
- Không lấy chữ nằm trong mockup làm ảnh giao diện; chữ và control phải được dựng bằng Razor/CSS để responsive và accessible.
- Ảnh nguyên liệu nằm trong `wwwroot/images/generated/redesign-2026` và được dùng trực tiếp trong UI.
- Loading dùng skeleton có cùng geometry với nội dung; không dùng spinner tròn.
- Crimson chỉ dùng cho primary action, active navigation, playback progress và trạng thái nguy cấp.
- Motion gồm cinematic fade-through và parallax nhẹ; mọi motion phải có reduced-motion fallback.

## Visual extraction

| Surface | Layout priority | Translation measurements |
|---|---|---|
| Home | Full-bleed backdrop, copy trái, rail portrait bên dưới | Hero khoảng 60-65% chiều cao first view; CTA nhìn thấy ngay; poster 2:3; gutter 48-64px |
| Explore | Search/filter phía trên, gallery có một landscape spotlight | Search 48-56px; filter một hàng desktop; image ratio cố định; caption ngoài ảnh |
| Genres | Một landscape featured cùng category gallery lệch tỷ lệ | Tile featured khoảng 60% chiều ngang; các tile còn lại theo grid; mobile một cột |
| Random | Copy trái, một recommendation lớn bên phải | Không wheel/dice; CTA chọn phim tách rõ CTA xem phim; title không quá hai dòng |
| Detail | Poster + title/actions trên backdrop, tab rail, body mở | Poster 2:3; hero 380-440px ở 1366px; content chia khoảng 2:1 |
| Watch | Player ưu tiên tuyệt đối, drawer tập ở phải | Player/drawer khoảng 72:28; frame 16:9; controls target tối thiểu 44px |
| Library | Poster grid hoặc featured resume + compact history rows | Portrait 2:3, still 16:9; viewing progress chỉ dùng khi dữ liệu có tiến độ |
| Account | Header identity, quick-nav, membership + payment history | Không thêm devices/session feature; nội dung auth thật; payment value từ API |
| Auth | Photograph trái khoảng 58%, form phải khoảng 42% | Form max-width 460px; input 48-56px; labels trên input; error dưới input |
| Settings | Account rail + hai nhóm controls | Rail khoảng 220px; toggle có label/help; save success inline |
| VIP | Clean cinematic hero, benefits riêng, chọn plan bên dưới | Pricing cùng radius; plan selection crimson; copy/price lấy API |
| Admin | Sidebar + KPI band + table dominant | Rail khoảng 220px; row đủ đọc; không biến dashboard thành marketing page |
| Error | Recovery copy và CTA, một cinema image có negative space | Không stack trace; không false promise; chỉ recovery action đã hỗ trợ |

Các kích thước trên là spec dịch bố cục từ ảnh, không phải số đo pixel tuyệt đối của model.

## Corrections before production

Image generation đôi khi tự thêm copy, thay wordmark/font hoặc metadata. Không đưa lỗi này vào sản phẩm:

- Navbar canonical giữ nguyên: Trang chủ, Khám phá, Thể loại, Tủ phim. Giữ notification/account menu và quyền truy cập hiện có.
- Giữ `CinemaLogo`/wordmark hiện hữu; không lấy logo khác kiểu từ một mockup riêng lẻ.
- UI dùng một font grotesk nhất quán có glyph tiếng Việt. Không copy serif ngẫu nhiên ở VIP/404. Movie key art được phép có title treatment riêng.
- Bỏ quote, slogan, chữ trang trí tự sinh trên ảnh và các góc trang. Asset clean không chứa chữ.
- Account dùng bản v2, không xây tính năng thiết bị/session được minh họa trong bản v1.
- Login giữ label Email hoặc Tên đăng nhập và quick-fill thử nghiệm hiện có. Register giữ field order và nguyên văn consent.
- Reset giữ label Nhập lại mật khẩu và CTA Đổi mật khẩu từ code. Không đổi token/email flow.
- Form error/success phải là state thực, không đồng thời hiện success với error. Mockup có state minh họa không phải default.
- Không hardcode tên, giá, thống kê, phim hoặc payment từ mockup. Các con số minh họa không phải dữ liệu production.
- Tủ phim không thêm filter/sort vô tác dụng hoặc Continue Watching nếu dữ liệu không hỗ trợ.
- CTA recovery chỉ render thao tác có thể thực hiện. Không tự mở rộng API/logic để chiều artwork.

## Fidelity gate for implementation

1. Chốt bản mẫu với người dùng trước khi thay UI ứng dụng. Đây là phase phác thảo, không phải phase implement.
2. Render mẫu final section/mobile thêm nếu một vùng chưa đủ rõ; không crop mockup cũ thành asset nguyên liệu.
3. Dùng asset clean trực tiếp. Text/control phải là Razor/CSS, không lấy cả screenshot làm giao diện.
4. Capture implementation cùng viewport desktop mẫu, thêm 1366x768, 1440x900 và mobile 390x844.
5. So từng page: silhouette/layout, image crop/focal point, hero height, gutters, type size/wrap, button geometry, surface/accent.
6. Sửa mọi sai khác lớn. Lưu screenshot đối chiếu và checklist từng route, không chỉ kiểm tra Home.
7. Test loading, empty, error, validation, logged-out, FREE/VIP/admin, keyboard focus và reduced-motion.
8. Chạy `dotnet test` 100% pass và `dotnet build WebXemPhim.slnx` 0 error trước khi bàn giao.

Không khẳng định pixel-perfect chỉ dựa vào ảnh sinh. Fidelity chỉ được xác nhận sau khi có website thật và screenshot đối chiếu.

## Generation provenance

Dùng built-in ImageGen, không dùng CLI/API fallback. Prompt set nằm trong `PROMPTS.md`.
Ảnh mockup ở thư mục này; 6 asset clean ở `src/Client/WebXemPhim.Client/wwwroot/images/generated/redesign-2026`.
