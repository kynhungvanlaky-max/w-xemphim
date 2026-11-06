# 🐌 Báo cáo Audit Lag — Trang Admin & Các Popup

> Phạm vi: `src/Client/WebXemPhim.Client` (khu Admin) + `src/Server/WebXemPhim.Server/Controllers/Admin`
> Ngày rà soát: 2026-11-06 · Snapshot code: commit `7d5db14` + working tree hiện tại
> Phương pháp: đọc code trực tiếp (không đo runtime). Số dòng code tham chiếu có thể lệch khi file thay đổi.
> Mức độ: **P0** = treo/lag rõ ràng · **P1** = lag cảm nhận thường xuyên · **P2** = tăng dần theo thời gian / nhẹ.

---

## 1. Bối cảnh kiến trúc (tại sao Admin dễ lag)

- `App.razor:58`: `<Routes @rendermode="InteractiveServer" />` → **toàn bộ trang Admin sống trên 1 circuit SignalR duy nhất**.
  - Mỗi lần `StateHasChanged()` phải diff render-tree và đẩy RenderBatch qua vòng SignalR; fetch HTTP của client cũng đi qua circuit.
  - Mọi con số "N lần render lại" dưới đây đều là N lần diff toàn bộ tree con + N gói mạng.
- Postgres đặt remote (Render / Supabase + pooler, RTT ~30–100ms) → **số round-trip tuần tự của mỗi endpoint là nguyên nhân chậm số 1 phía server**.
- Toàn bộ file `.razor` Admin viết logic inline (không có code-behind), trang lớn (AdminEpisodes ~3.000 dòng) → mỗi render diff đắt.

## 2. Client — Findings

### P0-1 · Upload video R2 (đến 4GB) bắn `StateHasChanged` mỗi chunk
`Components/Pages/Admin/AdminEpisodes.razor:1978-1985` (bản sao tại `:2588-2595`):

```csharp
await using var stream = selectedEpisodeVideoFile.OpenReadStream(maxAllowedSize: 4L * 1024 * 1024 * 1024);
var progress = new Progress<double>(p => {
    episodeUploadProgress = Math.Round(p, 0);
    InvokeAsync(StateHasChanged);   // ← mỗi chunk đọc
});
```

Nguồn gốc: `AdminApiClient.cs:706-728` — `ProgressReportingStream.Report(read)` được gọi **trong mỗi lần `ReadAsync`** → hàng chục–hàng trăm nghìn callback; mỗi callback diff toàn trang AdminEpisodes + đẩy RenderBatch.

**Sửa:** throttle progress theo % nguyên (hoặc 300ms); đẩy upload sang JS interop (XHR/fetch tới presigned URL — `AdminApiClient.cs:212-223` đã có `GetPresignedUploadUrlAsync`) để byte file không đi qua circuit.

### P0-2 · `GetXxxAsync()` nạp 1.000 bản ghi full-detail chỉ để render ít
`AdminApiClient.cs:376-380 / :471 / :499 / :526` — **4 API** (movies, users, payments, reviews) đều `pageSize: 1000`:

```csharp
var paged = await GetMoviesPagedAsync(page: 1, pageSize: 1000, ...);
```

Movie trả `MovieDetailDto` (kèm `Episodes` + `StreamSources` + `Cast`) → vài chục MB JSON mỗi lần:

- `AdminOverview.razor:419/425` → chỉ dùng với `.Take(6)` tại `:237` (6 dòng "recent movies"!).
- `AdminEpisodes.razor:1584` — `allMoviesList` fetch full list mỗi lần vào trang.

**Sửa:** endpoint trả DTO lõi (id/title/poster/type/count); overview gọi `pageSize:6`; picker gọi paged + `q=` server-side (đã hỗ trợ).

### P0-3 · Preview thumbnail base64 ~6.8MB gắn vào render tree
`AdminEpisodes.razor:2155-2164` — `Convert.ToBase64String(buffer)` đặt thẳng vào `src` của `<img>` trong modal → từng byte đi qua RenderBatch.
**Sửa:** JS interop `URL.createObjectURL` cho preview.

### P0-4 · CSV export: tối đa 25 request tuần tự + 5.000 DTO trong circuit
`AdminVip.razor:1555-1577` — vòng `while` fetch `pageSize:200` tới 5.000 PaymentDto rồi stringify trong code-behind → UI đông cứng nhiều giây.
**Sửa:** endpoint `/export` phía server stream file trực tiếp.

### P1-1 · Auto-refresh AuditLogs: Timer + `finally StateHasChanged()` vô điều kiện
`AdminAuditLogs.razor:621` (Timer 5/15/30s) + `:694-701`:

```csharp
finally {
    if (!silent) { isLoading = false; }
    StateHasChanged();   // ← render lại cả bảng 200 dòng mỗi tick, kể cả silent + không có gì đổi
}
```

Bảng không `@key`, mỗi dòng nhiều `<CinemaIcon>` (component). Cộng: `CountActions/GetActionPercent` duyệt `pageItems` **8 lần mỗi render** (`:109-134`).
**Sửa:** silent-mode chỉ rerender khi dữ liệu đổi; `@key="log.Id"`; tính stats 1 lần sau fetch.

### P1-2 · `Auth/Theme.OnChange` → rerender cả trang, kèm vòng retry 60s
`App.razor:77-80` + `AdminLayout.razor:128-137`: cả root lẫn layout đăng ký `OnChange → StateHasChanged`. `AdminLayout.razor:182-215` (`StartAccessRetry`): loop 2s × 30 lần gọi `Auth.RefreshAsync()` — mỗi tick fire cả 2 subscriber.
**Sửa:** `ShouldRender` guard khi giá trị chưa đổi; vòng retry giờ → phút và chỉ dừng khi lỗi thật.

### P1-3 · `AdminVip.LoadData` = 7 HTTP/pre-refresh, 4 call riêng chỉ để đếm
`AdminVip.razor:919-968` — `pageSize:1` × 4 (pending/completed/failed/all) chỉ lấy `TotalItems`.
**Sửa:** `stats` trả đủ count theo status (1 call), hoặc count tổng từ 3 KPI.

### P1-4 · Bulk user: vòng foreach tuần tự, không progress
`AdminUsers.razor:1274-1352` (lock/unlock/VIP/admin ×4 đoạn) — mỗi user 1 HTTP nối tiếp.
**Sửa:** endpoint `api/admin/users/bulk-action` (mẫu `movies/bulk-action` đã có); hoặc `Parallel` giới hạn + progress.

### P1-5 · Telemetry auto-refresh kép render
`AdminTelemetry.razor:384-402` — vòng `while` mỗi tick `RefreshAsync()` + `StateHasChanged()` (double render).
**Sửa:** như P1-1.

### P1-6 · Modal streams (AdminEpisodes): dồn fetch + probe tuần tự + double load
- `:2453` `_ = LoadServerPolicies()` fire mỗi lần mở modal.
- `:2483-2497` ProbeAllStreams tuần tự + `StateHasChanged()` **mỗi một result**.
- Lưu xong nạp lại 2 list (`:2623-2624`, `:2772-2773`) → 2 render.

**Sửa:** `Task.WhenAll` probe + 1 nguồn dữ liệu; gộp reload post-save.

### P1-7 · Bảng Admin thiếu `@key` + icon là component
Chỉ `AdminMovies.razor:410` có `@key="m.Id"`. AdminUsers / AdminVip / AdminAuditLogs / AdminEpisodes không có; mật độ `<CinemaIcon>` ~5-6/dòng.
**Sửa:** thêm `@key` mọi `<tr>`; cân nhắc icon tĩnh trong ô table.

### P2
- `AdminCommandPalette.razor:114-122` — `FilteredCommands` là property `get` + `ToList()` mỗi lần gọi (mỗi render 2 lần) + `mouseenter` set state → rerender. Nhẹ (13 mục) nhưng dễ sửa bằng cache field.
- `admin-workspace.css` — nhiều animation `infinite` (livePulse/shimmer) + `backdrop-filter: blur` trên modal (`:1256`, `:1967-1968`, `:2367`) → thêm repaint mỗi lần content dưới modal đổi.
- `AdminUsers.razor:1098-1107` & `AdminMovies.razor:1503-1513` — pagination handler `_ = LoadXxx()` fire-and-forget → double-click tạo race + flash.
- `AdminEpisodes` lọc + phân trang phía client trên full-list trong circuit (`:355`, `:415`) — ổn ở ~vài trăm tập, sẽ phình nếu catalog lớn.

### Đã kiểm tra & **KHÔNG** phải vấn đề
`SearchDebouncer` (debounce + CTS chuẩn); JS admin-tools chỉ 1 keydown listener, dispose đúng; `AdminConfirmDialog` render có điều kiện; `AdminPagination/Toast` dispose chuẩn; `CinemaHubClient` không refresh trang admin; không dùng `CascadingValue` trong khu Admin.

## 3. Server — Findings

### P0-S1 · `GET api/admin/stats` — ~18 round-trip tuần tự
`AdminOverviewController.cs:25-114`: 14 aggregate nối tiếp (một trong đó `vipMembers` trùng logic `activeVipUsers` :31/:33) + 3 recent-list + 1 GroupBy. Với RTT 50-150ms ⇒ 1-3s chỉ Roll-trip.
**Sửa:** 1 SQL conditional-aggregate (`COUNT(*) FILTER (WHERE ...)`) + `IMemoryCache` 30-60s cho dashboard.

### P0-S2 · Search users/reviews/payments tải nguyên bảng `users` vào RAM
Mùi: `users.Email/FullName` bị `FieldEncryptionConverter` mã hoá (`AppDbContext.cs:144-147`) → không dịch `Contains` xuống SQL được.

- `AdminUsersController.cs:126-151` — `materialized = await query.ToListAsync()` (cả bảng, **kèm PasswordSalt/Hash**) → filter/sort/paginate trong RAM. Còn `:542` một chỗ nữa `context.Users.ToListAsync()` khi grant VIP.
- `AdminReviewsController.cs:37`, `AdminPaymentsController.cs:40` — `await context.Users.AsNoTracking().ToListAsync()` cho 1 ô tìm kiếm.

**Sửa:** dùng `NormalizedEmail` (unique, đã có) cho email + thêm cột `SearchNameLower` (unaccent+lower, ghi khi tạo/cập nhật user) + GIN pg_trgm index; xoá nhánh in-memory khi cột sẵn sàng.

### P0-S3 · `GET api/admin/telemetry/summary` — 11 round-trip + P95 tải 50.000 hàng
`AdminTelemetryController.cs:22-139` — Count×4 + Average×2 riêng, timeline/eventTypes/topSources/recentErrors lần lượt; `MaxStartupSamplesForPercentile = 50_000` (`:19`) tải giá trị vào RAM để tính P95.
**Sửa:** 1 `SqlQueryRaw` gộp (`COUNT FILTER`, `AVG`, `percentile_cont(0.95) WITHIN GROUP`), 2 query phụ; thêm index `SessionId`.

### P1-S4 · `GET api/admin/movies` (list) Include 3 tầng + trả `MovieDetailDto`
`AdminMoviesController.cs:48-53` — `.Include(MovieGenres).ThenInclude(Genre).Include(CastMembers).Include(Episodes)` cho **màn hình danh sách**; chưa `AsSplitQuery` (server chỉ có 1 chỗ: `DatabaseMovieService.cs:404`); do đó "cartesian explosion" khi 20 phim × hàng chục tập.
Search `q` dùng `Title.ToLower().Contains` (`:55-63`) dù full-text `search_vector` + GIN **đã có** (migration `AddMovieSearchIndex`).
**Sửa:** list dùng projection `MovieListItemDto` (kèm `EpisodesCount`) ; split-query riêng cho popup chi tiết; `q` đổi sang FTS`SearchVectorMatches`.

### P1-S5 · Auth opaque-token: ~5 round-trip / request, không cache
`OpaqueBearerHandler.cs:34` → `AuthService.cs:367-399` (token+user, security) + `:473-490` (IsAdmin / MfaCredentials / MfaVerifiedSessions). Trang Admin gọi ~9 endpoint đồng thời ⇒ ~45 query chỉ để auth.
**Sửa:** `IMemoryCache` 30-60s (token hash → claims), evict khi revoke/lock/đổi role; hoặc gộp 3 query phụ thành 1 join.

### P1-S6 · `GET api/admin/users/{id}/details` ("full 360") — 6 round-trip nối tiếp
`AdminUsersController.cs:206-282`: FindAsync + security + 2 count + recent payments + recent reviews tuần tự → popup mở chậm 0.3-0.9s.
**Sửa:** `Task.WhenAll` (factory cho phép nhiều context song song); `AsNoTracking` cho phần đọc.

### P1-S7 · N+1 thể loại — mỗi "Lưu phim" chạy `AnyAsync(genre)` trong foreach
`AdminMoviesController.cs:198-207` (Create) & `:354-361` (Update). Mỗi save = check slug + N×genre + SaveChanges + audit SaveChanges + re-include trả về + refresh FTS ⇒ hàng chục RTT.
**Sửa:** 1 query `ids.Contains` → set hợp lệ; bỏ re-fetch Include sau save.

### P1-S8 · `AddEpisode` — fan-out notification (bulk INSERT + N lần SendAsync) nằm trong request
`AdminEpisodesController.cs:178-235` — join watchlist → AddRange N notification → SaveChanges → **foreach `Clients.Group(...).SendAsync` tuần tự**. Phim hot (1-5k watchlist) ⇒ "thêm tập" chậm hàng giây.
**Sửa:** gộp 1 lần send cho mảng userId khi DTO chung + Content, hoặc chuyển đẩy sang BackgroundService (`Channel<T>`).

### P1-S9 · Thiếu index cho sort/filter dùng nhiều (migration `AddAdminPerfIndexes`)
- `users`: không có `CreatedAtUtc` (sort mặc định mọi request) , `(Membership, VipExpiresAtUtc)` — `AppDbContext.cs:137-152` chỉ có unique-email + lookup-hash.
- `movies`: thiếu `ViewCount DESC`, `Rating DESC`, `Country`, `Title` (sort đơn).
- `payments`: thiếu `CreatedAtUtc DESC`, `(Status, CreatedAtUtc)` — `:168-182`.
- `reviews`: thiếu `CreatedAtUtc` — `:183-191`.
- `playback_telemetry`: thiếu `SessionId` (popup filter events).

### P2-S10 · Blocking file-I/O trong async request khi xoá
`AdminEpisodesController.cs:312-313` gọi `thumbnailStorage.Delete(...)` / `videoStorage.DeleteEpisodeVideos(...)` đồng bộ (`EpisodeVideoStorage.cs:213-239`, `EpisodeThumbnailStorage.cs:185-217`); `:318` load full `remaining` chỉ để lấy Max EpisodeNumber (dùng `MaxAsync` thay).
### P2-S11 · `audit-logs` search `ILIKE '%q%'` seq-scan, bảng chỉ tăng trưởng
`AdminOverviewController.cs:216-248` — index sort đã có; search Summary/AuditEvents chưa; cần pg_trgm + chính sách retention (90 ngày) để không chậm dần theo tháng.
### P2-S12 · `system/health` 4 count + 1 raw SQL riêng
`AdminOverviewController.cs:142-203` — gộp 1 dòng; `activeStreamsCount` đang đếm `StreamSources` (sai ngữ nghĩa — đúng là `ActiveStreams WHERE IsActive && ExpiresAtUtc > now`).

### Cache — tình trạng hiện tại
`Program.cs:61-66` chỉ có 3 policy OutputCache cho **endpoint công khai**. Admin GET **không cache nào** (đúng về freshness). Có thể thêm TTL ngắn 30-60s cho `stats` / `system/health` / `telemetry/summary` với tag riêng + evict tại hook audit-log chung để cache không bị stale.

## 4. "Vì sao lag" — kịch bản điển hình

| Kịch bản | Dây chuyền chậm |
|---|---|
| Vào `/admin` (Overview) | `stats` 18 RTT + `GetMoviesAsync(1000 detail)` (P0-2) + Auth × nhiều call (S5) |
| Mở popup User detail | 6 RTT tuần tự (S6) + modal backdrop-filter repaint |
| Search users/reviews/payments trong popup | fetch full bảng `users` + giải mã từng dòng (S2) |
| Lưu phim / thêm tập | N+1 genre (S7) / fan-out SignalR (S8) + audit + re-include → hàng chục RTT |
| Upload video/thumbnail | bão `StateHasChanged` (P0-1, P0-3) |
| Treo tự nhiên khi đứng yên | Timer AuditLogs/Telemetry rerender mỗi tick (P1-1, P1-5) + Auth/Theme OnChange (P1-2) |

## 5. Kế hoạch ưu tiên (gợi ý đặt hàng sửa)

**Wave 1 — Quick wins (≤1 ngày, giảm "treo" rõ nhất):**
1. Throttle upload progress + chuyển upload R2 sang JS interop (P0-1).
2. `pageSize:6` cho overview + endpoint trả DTO lõi; bỏ full-list ở AdminEpisodes picker (P0-2).
3. Xoá unconditional `StateHasChanged` trong `finally` silent-mode + `@key` (P1-1/P1-7).
4. `Task.WhenAll` cho `users/{id}/details` + probe streams song song (P1-S6, P1-6).

**Wave 2 — Server refactor (2-3 ngày):**
5. 1-round-trip dùng `SqlQueryRaw` cho `stats` / `telemetry/summary` / `system/health` (+`IMemoryCache` TTL ngắn) (P0-S1, P0-S3, P2-S12).
6. `SearchNameLower` + bỏ nhánh in-memory search (P0-S2).
7. Auth token cache 30-60s (P1-S5).
8. `MovieListItemDto` + `AsSplitQuery` + search FTS cho admin movies (P1-S4).

**Wave 3 — Data hygiene:**
9. Migration `AddAdminPerfIndexes` + pg_trgm cho search (P1-S9, P2-S11).
10. Đưa fan-out notification + delete-storage vào background job (P1-S8, P2-S10).

## 6. Cách xác nhận sau khi sửa (plan đo)

1. **Client:** DevTools → Network/WS: đếm RenderBatch; Blazor circuit "Incoming bytes" giảm; `Response` time per endpoint giảm.
2. **Server:** app log + `EXPLAIN (ANALYZE, BUFFERS)` cho `stats`, `telemetry/summary`, admin `users` search; đếm round-trip/Npgsql log khi logout.
3. **Đặt kỳ vọng:** vào `/admin` < 1s (local)/< 2s (remote); mở popup < 300ms; save phim không fan-out > 3 RTT.

---

## 7. Trạng thái triển khai (2026-11-06)

Đã hoàn tất toàn bộ 3 Wave. Build `WebXemPhim.slnx` 0 Error, `dotnet test` 637 PASS / 1 SKIP / 638.

### Wave 1 — Quick wins (client)
- `ProgressReportingStream` chỉ báo tiến độ khi phần trăm nguyên thay đổi (~100 callback thay vì mỗi buffer).
- AdminOverview chỉ tải 6 phim mới nhất ở chế độ `summary` thay vì 1.000 full-detail.
- AdminVip: KPI đếm trạng thái bằng 1 endpoint `payments/summary` (thay 4 request), export CSV xử lý phía server.
- AdminUsers/AdminMovies: ticket-guard chống race khi gõ tìm kiếm nhanh; `@key` cho danh sách người dùng.
- AdminAuditLogs: auto-refresh im lặng bỏ qua re-render khi dữ liệu không đổi, cache số lượng theo mức rủi ro, `@key` cho timeline/bảng.
- AdminEpisodes: preview thumbnail bằng object URL (không đọc base64 cả file), `Task.WhenAll` cho probe toàn bộ luồng / mở stream manager / tải lại sau upload R2.
- AdminCommandPalette: memoize danh sách lệnh đã lọc theo từ khoá.
- `admin-tools.js`: thêm `previewImageFromPicker` + `revokeObjectUrl`; `downloadCsv` không chèn trùng BOM.

### Wave 2 — Server refactor
- `AdminOverviewController.GetStats`: `IMemoryCache` TTL 30s + 8 nhóm truy vấn song song; `activeStreamsCount` dùng `ActiveStreams` còn hiệu lực.
- `AdminTelemetryController.GetSummary`: 6 nhóm truy vấn song song; P95 tính bằng `percentile_cont(0.95)` trên PostgreSQL.
- `AdminUsersController`: search SQL qua `SearchNameLower`/`NormalizedEmail`, sort tên ở SQL; `GetUserDetail` chạy song song với DbContext riêng; endpoint mới `POST users/bulk-action`.
- `AdminPaymentsController`: search SQL, endpoint mới `GET payments/summary` và `GET payments/export-csv` (CSV escape an toàn qua `CsvSecurity`).
- `AdminReviewsController`: search SQL qua cột chuẩn hoá.
- `AdminMoviesController`: tham số `summary` trả DTO nhẹ, `AsSplitQuery`, sửa N+1 genre khi Create/Update.
- `AdminEpisodesController`: fan-out thông báo và xoá storage chạy nền; `MaxAsync` thay vì nạp toàn bộ tập.
- `AuthSessionCache` TTL 30s: validate token nhanh, evict theo token/user khi logout, đổi/reset mật khẩu, khoá tài khoản, đổi quyền/membership, duyệt thanh toán.
- AdminMovies/AdminUsers/AdminPayments/AdminReviews dùng chung `AdminApiClient` đã bổ sung `BulkUserActionAsync`, `GetPaymentSummaryAsync`, `ExportPaymentsCsvAsync`.

### Wave 3 — Data hygiene
- Migration `20261105175937_AddAdminPerfIndexes`: cột `users.SearchNameLower`, index `(SearchNameLower, CreatedAtUtc)`, `reviews.CreatedAtUtc`, `playback_telemetry.SessionId`, `payments.CreatedAtUtc`, `payments(Status, CreatedAtUtc)`, `movies(ViewCount/Rating/Country)`.
- `DatabaseSchemaHelper`: backfill `SearchNameLower` và tạo GIN trigram (`pg_trgm`) lúc khởi động, có try/catch để không chặn app khi thiếu extension.
- Đăng ký/cập nhật hồ sơ tự ghi `SearchNameLower`.

### Kiểm thử bổ sung
- `BulkUserAction_LockGrantAndRevokeVip_WorksInSingleRequest`
- `BulkUserAction_EmptyIds_ReturnsBadRequest`
- `GetPaymentSummary_ReturnsCountsForCurrentScope`
- `GetPaymentSummary_NoPayments_ReturnsZeroedSummary`
- `ExportPaymentsCsv_ReturnsCsvWithHeaderAndRows`
- Cập nhật test fan-out thông báo tập mới sang chờ nền (poll) thay vì assert đồng bộ.
