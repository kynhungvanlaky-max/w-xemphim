# Kế hoạch sửa triệt để các vấn đề còn lại – WebXemPhim

> Baseline trước đợt này: `dotnet build WebXemPhim.slnx` = 0 lỗi, `dotnet test` = 1141/1141 pass.
> Kết quả hiện tại: build 0 warning / 0 error, `dotnet test` = **1160/1160 pass**.
> Mọi thay đổi đều được kiểm chứng lại bằng test tự động + chạy thật Client/Server trên trình duyệt.

---

## Tổng kết trạng thái

| Phase | Nội dung | Trạng thái |
|---|---|---|
| Phase 1 | MySQL/relational search normalization (`movies.SearchText`) | ✅ Hoàn tất |
| Phase 1b | **Bug thật**: SQLite FTS không bỏ được chữ "Đ" → tìm "hanh dong" không ra "Hành Động" | ✅ Đã sửa |
| Phase 2 | Rate limit anonymous (M2) – forward IP thật từ Blazor sang API | ✅ Hoàn tất |
| Phase 3 | Schema: EF migration + one-shot maintenance + bỏ mã chết | ✅ Hoàn tất |
| Phase 4 | Tài nguyên tĩnh: ảnh 14.75MB → **8.17MB**, CSS theo route, bỏ Google Fonts, **bật Brotli/Gzip cho toàn bộ Client** | ✅ Hoàn tất |
| Phase 5 | Static SSR / streaming rendering | ⚠️ **Hoàn tất 1 phần** (xem ghi chú) |
| Phase 6 | Minor polish (VIP cache, FTS cast, mã chết, `catch {}`) | ✅ Hoàn tất |
| Ngoài lề | **`PersistentComponentState` sai cách dùng → WebSocket bị đóng, trang mất interactivity** | ✅ Đã sửa |

---

## Phase 1 – Search chuẩn hóa (đã xong)

- Thêm cột `movies.SearchText` (lowercase, bỏ dấu, gộp Title + OriginalTitle + Director + Cast + Overview).
  - Builder dùng chung: `src/Shared/WebXemPhim.Shared/Utils/MovieSearchTextBuilder.cs`.
- Migration `AddMovieSearchText` viết provider-aware (SQLite `TEXT` + index, MySQL `longtext` + prefix index 191).
- `DatabaseSchemaHelper` tự thêm cột + index + backfill lúc khởi động (idempotent, không lỗi nếu DB cũ).
- `DatabaseMovieService.FilterMoviesAsync`: provider relational dùng `SearchText.Contains(...)` (đã bỏ `ToLower().Contains` + subquery `CastMembers.Any`).
- Admin create/update/clone tự sinh lại `SearchText`.
- Seeder gọi `PopulateSearchTextAsync` sau khi tạo phim.

### Bug thật phát hiện thêm (S2 bản SQLite)

SQLite FTS5 tokenizer (`remove_diacritics 2`) **không bỏ được chữ Đ/đ** (không phải ký tự có dấu hợp thành).
Trigger insert thô → tìm "hanh dong" **không** ra "Hành Động".

Cách sửa: `normalized_content` của FTS lấy từ cột `SearchText` đã chuẩn hóa sẵn, kèm trigger
`trg_movies_fts_search_update` đồng bộ lại khi `SearchText` thay đổi.

Kiểm chứng (API thật):

```
hanh dong   => total=1  (Chuyện Tàu Sinh Tử)
hành động   => total=1
ky si       => total=4  (Kỵ Sĩ Bóng Đêm)
oppenheimer => total=1
zzz...      => total=0
```

---

## Phase 2 – Rate limit anonymous (M2, đã xong)

Nguyên nhân gốc: Blazor Server gọi API từ server nên API thấy **một IP chung** của host → mọi khách
ẩn danh dùng chung 1 bucket 300 req/phút.

Cách sửa: `ForwardedClientIpHandler` (Client) tính lại IP thật của người dùng rồi gửi kèm
`X-Forwarded-For` sang API. API vốn đã `UseForwardedHeaders` + `GetClientIpAddress` nên tự phân
bổ theo IP thật.

- Không dùng `UseForwardedHeaders` ở Client (gây đóng WebSocket SignalR) → logic nằm gọn trong handler.
- Header luôn được **ghi đè** ở server side ⇒ không thể giả mạo từ trình duyệt.
- Chỉ tin `X-Forwarded-For` khi request đến từ proxy đáng tin (loopback / private range).

---

## Phase 3 – Schema (đã xong)

- Migration `AddMovieSearchText`, `DropActiveStreamAliasColumns` (+ bảng `app_maintenance_flags`).
- `DatabaseSchemaHelper` tự tạo bảng/cột/index còn thiếu ⇒ DB cũ nâng cấp an toàn, vẫn idempotent.
- Dọn token cũ **chạy 1 lần** (gắn cờ `app_maintenance_flags`), không quét lại toàn bảng mỗi lần boot.
- Dọn URL ảnh `.jpg` → `.webp` **1 lần** (chỉ khớp đúng pattern seed, ảnh admin tự upload không bị đụng).
- Bỏ 2 cột "ma" do alias property (`DeviceName`, `LastHeartbeatAtUtc`) gây lỗi `no such column` khi
  query bảng `active_streams` trên DB cũ.

---

## Phase 4 – Tài nguyên tĩnh (đã xong)

| Hạng mục | Trước | Sau |
|---|---|---|
| `wwwroot/images` | 14.75 MB | **8.17 MB** (toàn bộ ảnh chuyển WebP q70) |
| Số file PNG/JPG lớn | 24 PNG > 500KB | 0 |
| CSS nạp ở mọi trang | 14 file (~730KB) | 7 file global + CSS theo route |
| Google Fonts (render-blocking, cross-origin) | có | **đã bỏ** (self-host đủ 4 weight + JetBrains Mono) |
| Nén response của Client | không | **Brotli + Gzip** |
| HTML trang chủ (transfer) | ~119 KB | **~12 KB** (br) |
| Ảnh trang chủ | nhiều PNG 2–3MB | WebP ~40–190KB |

- `RouteStylesheet.razor` tiêm CSS theo route vào `<head>` (Home, Explore, Genres, Watchlist, Watch,
  ProfileSettings, AccountWorkspace) – vẫn dùng `@Assets[...]` nên có fingerprint.
- Test `Pillar4_StaticImageLinks_ZeroBroken404References` vẫn xanh (mọi tham chiếu ảnh còn tồn tại).

---

## Phase 5 – Static SSR / streaming (hoàn tất 1 phần)

- **Đã làm**: `PersistentComponentState` dùng sai (inject service toàn cục thay vì property
  `[PersistentState]`) ⇒ state không được persist ⇒ API bị gọi 2 lần **và** circuit Blazor bị đóng
  ngay ⇒ **mất toàn bộ interactivity** (bấm nút không ăn). Đã sửa: 1 API call / 1 lần tải, WebSocket ổn định.
- **Chưa làm (cố ý defer)**: `[StreamRendering]` + Static SSR. Lý do: script inline do streaming sinh ra
  bị CSP `script-src 'self' 'nonce-…'` chặn ⇒ trang bị thiếu nội dung. .NET 10 không expose API công khai
  để gắn nonce vào script streaming, mà nới CSP thì phá vỡ posture bảo mật hiện tại.
  Thay vào đó đã bù lại bằng: Brotli (HTML 119KB → 12KB), CSS theo route, ảnh WebP, 1 API call.
  → Đề xuất: nếu sau này muốn Static SSR thì phải chọn 1 trong 2: (a) tự host script streaming,
  (b) dùng render mode `@rendermode="new InteractiveServerRenderMode(prerender:false)"` cho trang tĩnh.

---

## Phase 6 – Minor (đã xong)

- Cache `IsCallerVipAsync` 30s theo user (giảm 1 query DB mỗi lần mở trang chi tiết).
- FTS trigger đồng bộ `cast_members` khi cast thêm/sửa/xoá.
- Bỏ toàn bộ `catch { }` rỗng (0 kết quả grep).
- Xoá mã chết `Controllers/AdminController.cs`.
- `ApplicationStopping` không còn log cả stack trace.

---

## Kiểm chứng

- `dotnet build WebXemPhim.slnx` → **0 warning, 0 error**.
- `dotnet test` → **1160/1160 pass** (1141 cũ + 19 mới trong `RemainingAuditFixTests`).
- Chạy thật Client + Server:
  - Trang chủ: 48 ảnh, WebSocket ổn định, toggle theme hoạt động, không còn lỗi console.
  - `/explore`, `/genres`, `/watchlist`, `/account`, `/vip`: render đủ, không lỗi console.
  - Tìm kiếm API: `hanh dong` / `hành động` / `ky si` / `oppenheimer` trả kết quả đúng.

## Test mới (`tests/WebXemPhim.Tests/ServerTests/RemainingAuditFixTests.cs`)

1. `MovieSearchTextBuilder` bỏ dấu đúng (kể cả Đ/đ).
2. Tìm phim bằng từ khóa không dấu ("hanh dong") trên SQLite.
3. Tìm phim bằng từ khóa có dấu ("dat rung").
4. Tìm theo diễn viên.
5. Schema helper tạo cột + index `IX_movies_SearchText`.
6. Schema helper backfill `SearchText`.
7. `EnsureTablesCreatedAsync` idempotent.
8. Migrate URL ảnh `.jpg` → `.webp` (không đụng ảnh custom).
9. Cờ maintenance chống chạy lặp.
10–12. `ForwardedClientIpHandler`: forward IP thật, ghi đè header giả mạo, dùng XFF khi proxy tin cậy, không làm gì khi không có HttpContext.
13. Trang chủ nạp CSS theo route.
14. Trang explore nạp CSS riêng.
15. Response của Client được nén.
16. `wwwroot/images` < 10MB.
17. Không còn ảnh `.jpg` trong thư mục images.
18. Mọi tham chiếu ảnh trong razor/css đều tồn tại.
19. RouteStylesheet render link stylesheet.

---

## Việc còn lại (không làm được an toàn trong đợt này)

| Hạng mục | Vì sao | Hướng xử lý đề xuất |
|---|---|---|
| Static SSR / streaming rendering | Script streaming bị CSP nonce chặn, .NET 10 chưa có API gắn nonce | Chọn (a) self-host script streaming hoặc (b) bỏ prerender cho trang tĩnh, sau đó mới mở Static SSR |
| Minify CSS | Không có minifier an toàn trong toolchain, rủi ro hỏng style | Dùng `WebOptimizer`/`NUglify` ở bước build + kiểm thử snapshot CSS |
| Gộp `light-theme.css` (336KB) vào CSS variables | Refactor lớn, rủi ro sai màu Light/Dark | Làm dần theo từng nhóm component, mỗi nhóm có test chụp màn hình |
| Rate limit per-user thay vì per-IP | Cần đổi kiến trúc gọi API của trình duyệt | Khi nào scale nhiều server, chuyển public API sang gọi trực tiếp từ browser |
| MySQL FULLTEXT index | Cần MySQL thật để kiểm thử | Testcontainers trong CI trước khi bật trong production |
