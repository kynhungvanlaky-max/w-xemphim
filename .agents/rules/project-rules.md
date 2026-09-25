# WebXemPhim Project Rules & Guidelines

Dự án **WebXemPhim** là ứng dụng xem phim trực tuyến hiện đại được xây dựng hoàn toàn bằng **C# (.NET 10)** với kiến trúc tách biệt rõ ràng giữa **Server (REST API)** và **Client (Blazor Web App)**.

---

## 1. QUY TẮC PHÁT TRIỂN & KIỂM THỬ (BẮT BUỘC)

### 1.1. Khi sửa code: Bắt buộc tất cả Testcase phải PASS
- Mỗi khi tạo mới hoặc sửa đổi logic (Models, Services, Controllers, State), PHẢI chạy bộ test:
  ```bash
  dotnet test
  ```
- **100% testcases phải PASS**. Không được phép commit, bàn giao hoặc kết thúc khi có bất kỳ testcase nào bị FAIL.
- Nếu có lỗi test: Phải phân tích nguyên nhân gốc rễ (Root Cause) và sửa ngay lập tức.

### 1.2. Trước khi kết thúc đoạn chat / lượt phản hồi: Bắt buộc Build lại Toàn bộ Project (Xử lý triệt để cho tới khi hết lỗi)
- Mỗi lần làm xong công việc hoặc trước khi kết thúc bất kỳ lượt phản hồi (turn) nào, PHẢI tiến hành chạy lệnh build lại toàn bộ solution:
  ```bash
  dotnet build WebXemPhim.slnx
  ```
- **Kết quả build phải đạt 100% thành công (0 Error, 0 Warning nếu có thể)**.
- Nếu phát sinh bất kỳ lỗi nào (bao gồm lỗi cú pháp C#, lỗi Razor, lỗi dependency hay lỗi tranh chấp file lock `MSB3021/MSB3027` do process đang chạy ngầm), PHẢI tiến hành phân tích, giải phóng process chiếm giữ lock hoặc sửa code liên tục cho tới khi **BUILD SUCCEEDED 100% (0 Error)** mới thôi. Tuyệt đối không được kết thúc lượt phản hồi khi build còn lỗi.

---

## 2. KIẾN TRÚC & PHÂN TÁCH SERVER - CLIENT

Dự án được chia tách cấu trúc module độc lập, rành mạch:

```
WebXemPhim/
├── src/
│   ├── Server/
│   │   └── WebXemPhim.Server/       # ASP.NET Core Web API (REST API C#)
│   │       ├── Controllers/          # RESTful Endpoints (Movies, Episodes, Genres, Watchlist)
│   │       ├── Services/             # Business Logic & Data Access
│   │       ├── Data/                 # DbContext / Mock Repositories
│   │       └── Program.cs            # API configuration, Swagger, DI
│   ├── Client/
│   │   └── WebXemPhim.Client/       # Blazor Web App (C# UI)
│   │       ├── Components/           # Cinema UI Components (Hero, MovieCard, Player, Modals)
│   │       ├── Pages/                # Blazor Pages (Home, Browse, MovieDetail, Watch, MyList)
│   │       ├── Services/             # ApiClient Services (call Server REST API)
│   │       ├── State/                # User & Player State Management
│   │       └── wwwroot/              # Cinema-grade Vanilla CSS & Assets
│   └── Shared/
│       └── WebXemPhim.Shared/       # DTOs, Enums, Request/Response Contracts
│           ├── DTOs/                 # MovieDto, EpisodeDto, WatchlistDto
│           ├── Enums/                # MovieGenre, QualityLevel, StreamServerType
│           └── Requests/             # SearchFilterRequest, AddToWatchlistRequest
└── tests/
    └── WebXemPhim.Tests/             # Unit & Integration Tests (xUnit / FluentAssertions)
        ├── ServerTests/              # API Controllers & Service Tests
        └── ClientTests/              # Client API Services & State Tests
```

- **Server**: Chỉ chịu trách nhiệm cung cấp REST API chuẩn (JSON), xác thực, phân quyền và dữ liệu. Không chứa logic Razor hay render UI.
- **Client**: Blazor Web App giao tiếp với Server hoàn toàn thông qua REST API (`HttpClient` / Typed API Clients).
- **Shared**: Class library chứa toàn bộ DTOs và Models dùng chung, đảm bảo Type-Safety tuyệt đối giữa Client và Server.

---

## 3. TIÊU CHUẨN THIẾT KẾ UI/UX (TUÂN THỦ TASTE-SKILL)

Toàn bộ giao diện người dùng phải tuân thủ nghiêm ngặt kỹ năng `taste-skill` (`design-taste-frontend`):

### 3.1. Design Read & 3 Dials Configuration
Trước khi tạo hoặc sửa đổi bất kỳ giao diện nào, phải khai báo:
- **Design Read**: `Reading this as: Cinema-grade streaming web app for movie enthusiasts, with an immersive dark-room aesthetic language, leaning toward obsidian/zinc palette + amber/crimson accent + high-contrast typography + restrained motion physics.`
- **3 Dials**:
  - `DESIGN_VARIANCE: 7` (Bố cục bất đối xứng, hero billboard hoành tráng, hàng cuộn ngang mượt mà, lưới bento đa dạng)
  - `MOTION_INTENSITY: 6` (Card hover zoom nhẹ 1.04x, backdrop blur fade-in, tactile active button push `scale-[0.98]`)
  - `VISUAL_DENSITY: 5` (Khoảng thở điện ảnh rộng rãi, typography phân cấp rõ rệt, poster tỉ lệ chuẩn 2:3)

### 3.2. Chống AI-Slop & Tiêu chuẩn thẩm mỹ
- **CẤM AI Purple / Blue gradient slop**: Không lạm dụng ánh sáng tím mộng mơ bừa bãi.
- **Bảng màu Điện ảnh (Cinema Palette)**:
  - Tông nền: Obsidian Deep Black (`#0B0C10`), Zinc Slate Dark (`#12141A`), Surface Card (`#1A1D24`).
  - Điểm nhấn (Accent): Cinema Crimson (`#E50914`) hoặc Amber Gold (`#F59E0B`) cho nút CTA chính và Badge HD/IMDb.
  - Viền & Phản xạ: `border-white/10`, `backdrop-blur-md` với ambient light phản chiếu từ poster.
- **Typography hiện đại**: Dùng sans-serif cao cấp (Outfit / Inter Display), tiêu đề tương phản cao, nhãn gọn gàng, WCAG AA contrast (tối thiểu 4.5:1).
- **Trạng thái tương tác đầy đủ**: Loading Skeleton khớp chuẩn kích thước thẻ poster (tuyệt đối không dùng spinner xoay tròn generic thô sơ), Empty State có minh họa và nút hành động, Error State thân thiện.
- **Trình phát video (Player)**: Giao diện cinema dark room, thanh điều khiển thông minh tự ẩn khi phát, chọn server dự phòng, chọn tập phim nhanh dạng offcanvas/drawer.
