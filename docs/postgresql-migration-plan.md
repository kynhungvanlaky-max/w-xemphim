# Kế hoạch chuyển đổi cơ sở dữ liệu: SQLite → PostgreSQL

> Trạng thái: **Hoàn tất** · Cập nhật: 2026-10-21
> Phạm vi: `src/Server`, `src/Shared`, `tests/`, `database/`, `docker-compose.yml`

---

## 1. Bối cảnh và mục tiêu

Ứng dụng CinemaFlow (Blazor Web App + ASP.NET Core Web API) hiện dùng **SQLite** làm
database runtime. SQLite hạn chế ở mức production: ghi tuần tự (single-writer), không có
full-text search chuẩn, `decimal` bị lưu dạng `TEXT` (sai số tiền VIP/coupon), không có
`row-level`/concurrency semantics như PG.

Mục tiêu: **chuyển toàn bộ ứng dụng sang PostgreSQL**, bỏ SQLite khỏi runtime, giữ nguyên
mọi hợp đồng API/DTO và hành vi nghiệp vụ hiện tại.

### Kết quả mong đợi

| # | Mục tiêu | Tiêu chí nghiệm thu |
|---|----------|---------------------|
| G1 | PostgreSQL là provider mặc định | `Database:Provider` mặc định `PostgreSql`; app khởi động được với PG thật |
| G2 | Không còn phụ thuộc SQLite ở runtime | Không còn package/reference/code path SQLite trong `src/` |
| G3 | Full-text search tương đương FTS5 | Tìm kiếm tiếng Việt không dấu vẫn khớp; giữ thứ tự relevance |
| G4 | Schema đúng ngữ nghĩa PG | `uuid`, `timestamptz`, `boolean`, `numeric(12,2)`, `bytea` |
| G5 | Test 100% xanh | `dotnet test` → 0 failed, và có test chạy trên PostgreSQL thật |
| G6 | Hạ tầng vận hành | docker-compose có service `postgres`; script SQL khởi tạo; docs cập nhật |

---

## 2. Khảo sát hiện trạng (đã xong)

### 2.1 Provider hiện tại

`DatabaseRegistration.cs` hỗ trợ `Sqlite` | `MySql`. Ứng dụng chạy `Sqlite`; nhánh `MySql`
đã có nhưng chỉ dừng ở DDL, **không** có full-text search.

### 2.2 Ba khối phụ thuộc SQLite

| Khối | Vị trí | Mức độ |
|------|--------|--------|
| **A. Đăng ký provider + connection string** | `DatabaseRegistration.cs`, `appsettings*.json`, `WebSecurityExtensions.cs`, `docker-compose.yml`, `.env.example` | 🟢 Dễ |
| **B. DDL thủ công + introspection** | `DatabaseSchemaHelper.cs` (15 cặp SQL SQLite/MySQL, `sqlite_master`, `PRAGMA table_info`, `CREATE INDEX IF NOT EXISTS`) | 🟡 Trung bình |
| **C. Full-text search FTS5** | `DatabaseSchemaHelper.cs` (virtual table + 7 trigger + `GROUP_CONCAT`), `DatabaseMovieService.cs` (`MATCH ... ORDER BY rank`, `BuildFts5Query`), `AdminMoviesController.cs` (3 call-site `IndexSingleMovieAsync`) | 🔴 Khó |

### 2.3 Hạ tầng test

- **1175 testcase**, chạy **tuần tự** (`DisableTestParallelization = true`).
- 3 tầng DB: EF **InMemory** (đa số) → SQLite `:memory:` (16 file) → SQLite **file** (1 file).
- `TestApiFactory` dùng **EF InMemory** (không phải SQLite) → phần lớn test không bị ảnh hưởng.
- **Không có** test nào chạy `Migrate()`; runtime dùng `EnsureCreated()` + `DatabaseSchemaHelper`.
- ~35 testcase assert trực tiếp hành vi FTS5 (virtual table, trigger, `bm25`, `sqlite_master`).

### 2.4 Rủi ro phát hiện thêm

| Rủi ro | Chi tiết | Xử lý |
|--------|----------|--------|
| 🔴 `decimal` lưu `TEXT` ở SQLite | `PriceVnd`, `Amount`, `DiscountValue` → so sánh/tính tiền sai | PG dùng `numeric(12,2)` |
| 🔴 `DateTime` không có `Kind` chuẩn | Npgsql yêu cầu `Kind=Utc` cho `timestamptz` | Thêm value converter ép UTC |
| 🟡 `ToLower()` trong LINQ phụ thuộc collation | SQLite `LOWER` chỉ fold ASCII; PG mặc định **case-sensitive** | Chuẩn hoá so khớp ở tầng .NET (đã có sẵn `SearchText`) |
| 🟡 Migration cũ sinh cho SQLite | Type mapping `TEXT/INTEGER/BLOB` | Sinh lại migration cho PostgreSQL |
| 🟡 Extension `unaccent` | Cần quyền `CREATE EXTENSION` | Trusted extension từ PG13; có fallback nếu thiếu quyền |

---

## 3. Thiết kế mục tiêu

### 3.1 Kiến trúc provider

```
DatabaseRegistration
  ├── "PostgreSql" (mặc định) ──▶ UseNpgsql(...)
  └── "MySql"                    ──▶ UseMySQL(...)   (giữ nguyên, secondary)
```

SQLite **bị loại bỏ khỏi runtime**. `DatabaseSchemaHelper` chuyển từ boolean `isSqlite`
sang phân nhánh theo provider thật (`PostgreSql` / `MySql`).

### 3.2 Ánh xạ kiểu dữ liệu

| Kiểu C# | SQLite (cũ) | PostgreSQL (mới) | Ghi chú |
|---------|--------------|-------------------|---------|
| `Guid` | `TEXT` | `uuid` | native, index nhỏ hơn nhiều |
| `DateTime` | `TEXT` | `timestamptz` | converter ép `Kind=Utc` |
| `bool` | `INTEGER` | `boolean` | native |
| `decimal` | `TEXT` ⚠️ | `numeric(12,2)` | **sửa lỗi số tiền** |
| `double` | `REAL` | `double precision` | |
| `byte[]` | `BLOB` | `bytea` | |
| `string` (maxlen) | `TEXT` | `varchar(n)` | |
| `string` (không maxlen) | `TEXT` | `text` | `SearchText` |

### 3.3 Full-text search: FTS5 → `tsvector` + GIN

**Bài toán:** FTS5 dùng `tokenize='unicode61 remove_diacritics 2'`, **không** fold được chữ
`Đ`. Giải pháp hiện tại là cột `normalized_content` chứa text đã chuẩn hoá sẵn ở .NET.

**Giải pháp PostgreSQL** (tương đương về hành vi, mạnh hơn về hiệu năng):

1. Cột `movies."search_vector" tsvector` — **GENERATED STORED**? Không: cast members nằm
   ở bảng khác nên không dùng được generated column. → Dùng **trigger**.
2. Hàm chuẩn hoá SQL `webxemphim_normalize(text)`:
   `unaccent()` (extension) → thay `Đ/đ` → `lower()` → gộp khoảng trắng.
   Có **fallback** không cần extension (chỉ thay `Đ/đ` + `lower`) nếu thiếu quyền.
3. Trigger `movies` (INSERT/UPDATE) → tính `search_vector` có trọng số:
   `Title` (A) + `OriginalTitle` (B) + `Director` (C) + `cast_members` (D) + `Overview` (E).
4. Trigger `cast_members` (INSERT/UPDATE/DELETE) → tính lại vector của phim cha.
5. GIN index trên `search_vector`.
6. Truy vấn: `to_tsquery('simple', @q)` với `@q` do **.NET** dựng (`token:*` nối bằng ` & `),
   nên không có SQL injection và khớp tiền tố (autocomplete) như FTS5.

**Vì sao vẫn khớp tiếng Việt không dấu:** cả vế index lẫn vế query đều đi qua cùng một bộ
quy tắc chuẩn hoá → `"Xứ Cát"` và `"xu cat"` cho cùng một kết quả.

### 3.4 Ưu tiên ưu tiên `relevance`

`ts_rank_cd(search_vector, q)` thay cho `bm25`. Thứ tự kết quả giữ nguyên tính chất:
tiêu đề khớp → xếp trước.

### 3.5 Chiến lược test

| Nhóm | Trước | Sau |
|------|-------|-----|
| Logic thuần .NET (~300 test) | không DB | **không đổi** |
| EF InMemory (~200 test) | InMemory | **giữ nguyên** |
| Relational/FTS/schema (~35 test) | SQLite `:memory:` / file | **PostgreSQL thật** qua Testcontainers |
| Assertion provider-specific | `sqlite_master`, `PRAGMA`, `movies_fts` | `information_schema`, `pg_indexes`, `search_vector` |

Hạ tầng mới: `PostgresTestDatabase` — **1 container** dùng chung cho toàn assembly, mỗi test
class tạo **database riêng** (nên `EnsureCreated()` hoạt động đúng) và tự drop khi kết thúc.

---

## 4. Kế hoạch thực hiện

### Phase 1 — Nền tảng provider
- [x] Khảo sát codebase, xác định 3 khối phụ thuộc SQLite
- [x] Dựng PostgreSQL 17 container để kiểm chứng hành vi (`unaccent`, `tsvector`, prefix)
- [x] Thêm package `Npgsql.EntityFrameworkCore.PostgreSQL`
- [x] `DatabaseRegistration`: provider `PostgreSql` (mặc định), loại bỏ `Sqlite`
- [x] `AppDbContext`: converter `DateTime` → UTC, map cột `search_vector`
- [x] `appsettings*`, `WebSecurityExtensions`, `docker-compose.yml`, `.env.example`

### Phase 2 — Schema & Full-text search
- [x] `DatabaseSchemaHelper`: nhánh DDL PostgreSQL (đúng kiểu PG)
- [x] `DatabaseSchemaHelper`: introspection PostgreSQL (`information_schema`, `pg_indexes`)
- [x] `DatabaseSchemaHelper`: `search_vector` + trigger + GIN index + `webxemphim_normalize`
- [x] `DatabaseMovieService`: nhánh tìm kiếm PostgreSQL, bỏ `BuildFts5Query`
- [x] `AdminMoviesController`: bỏ 3 call-site `IndexSingleMovieAsync` (trigger tự lo)
- [x] `Program.cs`: đồng bộ search index sau seed

### Phase 3 — Migration & vận hành
- [x] Sinh lại migration cho PostgreSQL (`InitialCreate` + `AddMovieSearchIndex`)
- [x] `database/postgresql/001_initial.sql` (script DDL tương đương `database/mysql`)
- [x] `docker-compose.yml`: service `postgres` + profile `postgres`
- [x] `database/README.md`, `docs/` cập nhật

### Phase 4 — Test
- [x] `PostgresTestDatabase` (Testcontainers) + `Testcontainers.PostgreSql`
- [x] Chuyển 16 file test phụ thuộc SQLite sang PostgreSQL
- [x] Viết lại ~35 assertion FTS5 → `search_vector`
- [x] Test mới: schema PG, search tiếng Việt, cascade delete, concurrency
- [x] `dotnet test` → **0 failed**

### Phase 5 — Hoàn tất
- [x] `dotnet build WebXemPhim.slnx` → 0 error, 0 warning
- [x] Xác nhận không còn tham chiếu SQLite trong `src/`
- [x] Smoke test: `dotnet run` server + client với PostgreSQL thật

---

## 5. Rủi ro & biện pháp giảm thiểu

| Rủi ro | Mức | Biện pháp |
|--------|-----|-----------|
| Testcontainers cần Docker | Cao | Image `postgres:17-alpine` đã cache; CI bật service Docker |
| `unaccent` không cài được | Trung bình | Fallback `webxemphim_normalize` không extension + log cảnh báo |
| `tsvector` map sai kiểu trong EF | Trung bình | `HasColumnType("tsvector")` + `ValueGeneratedOnAddOrUpdate`; test model metadata |
| Trigger không khớp với `EnsureCreated` | Trung bình | `DatabaseSchemaHelper` tạo trigger **sau** `EnsureCreated`, trước seeding |
| Chuyển đổi `decimal` làm lệch dữ liệu cũ | Thấp | Dữ liệu demo seed lại được; script migration có bước `ALTER` |
| `ToLower()` case-sensitive ở PG | Trung bình | Chuẩn hoá so khớp ở .NET; `SearchText` là nguồn tra cứu chính |

---

## 5b. Kết quả thực thi

| Hạng mục | Kết quả |
|----------|---------|
| Build | `dotnet build WebXemPhim.slnx` → **0 error, 0 warning** |
| Test | `dotnet test` → **1188 / 1188 PASS** (trước: 1175, thêm 13 test mới) |
| Tham chiếu SQLite còn lại trong `src/` | **0** |
| Migration trên DB trống | `InitialCreate` → `AddMovieSearchIndex` → `AddSecurityAuditEventUserForeignKey` ✅ |
| Script `database/postgresql/001_initial.sql` | Chạy 3 lần liên tiếp → **0 lỗi** (idempotent) |
| Smoke test | Server khởi động, seed 40 phim, tìm kiếm có/không dấu ✅ |

### Các phát hiện kỹ thuật trong quá trình chuyển đổi

1. **PostgreSQL kiểm tra khóa ngoại lúc `CREATE TABLE`** (khác SQLite) → runtime chuyển từ
   `EnsureCreated()` sang `Migrate()`, và helper bỏ qua DDL phụ thuộc khi bảng gốc chưa tồn tại.
2. **`SqlQueryRaw<T>` bọc query thành `SELECT s."Value" FROM (...) AS s`** → cột phải được đặt
   tên `Value`, và **không được có dấu `;` cuối**; các truy vấn có tham số được chuyển sang ADO.NET.
3. **Tên index không quote sẽ bị hạ chữ thường** → toàn bộ index của helper được quote để giữ
   đúng tên `IX_<Bảng>_<Cột>` theo quy ước của EF Core.
4. **Identity sequence không tự tiến khi insert ID tường minh** → thêm `SyncIdentitySequencesAsync()`
   sau khi seed, nếu không phim tạo mới sẽ đụng khoá chính.
5. **Model EF khai báo thiếu quan hệ với `security_audit_events`** trong khi DDL của helper có
   khoá ngoại → đã bổ sung quan hệ `SetNull` trong model (và migration), giúp EF sắp xếp thứ tự
   insert đúng.
6. **`unaccent` là trusted extension** nhưng môi trường managed có thể không cho phép → hàm
   `webxemphim_normalize()` kiểm tra extension lúc chạy, không lúc tạo, nên không bao giờ hỏng.


### 5c. Sửa lỗi FOUC: route stylesheet mất khi điều hướng client-side

**Triệu chứng:** mở web lần đầu (hoặc click link trong app) → trang hiển thị **không có style**
(còn nav bar thì vẫn đẹp). F5 lần sau thì bình thường.

**Nguyên nhân:** `RouteStylesheet` đưa CSS route vào `<head>` qua `HeadContent`. Cơ chế này
chỉ được cập nhật **khi server render trang đó lần đầu**. Khi điều hướng client-side (click
link), `<link>` được thêm vào head **sau khi trang đã vẽ**, nên trình duyệt kịp vẽ trang
chưa có style → CSS tải bất đồng bộ → nháy trang "vỡ". Lần sau CSS đã nằm trong cache
(`immutable`, 1 năm) nên nhìn thấy bình thường.

**Đo được trước khi sửa:**

| Điểm đo | Trước | Sau |
|----------|-------|-----|
| `grid` của `.genres-bento-grid` | `block` | `grid` |
| `position` của `.genre-card` | `static` | `relative` |
| `border-radius` của `.genre-card` | `0px` | `18px` |
| kích thước `.genre-poster-thumb` | `0px` | `36px` |

**Cách sửa (giữ nguyên nguyên tắc "chỉ tải CSS của trang đang xem"):**
1. `RouteStylesheetCatalog` là nguồn duy nhất của ánh xạ route → CSS (các trang dùng chung).
2. `App.razor` xuất bản đồ route → URL asset có fingerprint dưới dạng
   `<script id="route-style-map" type="application/json">`.
3. `js/route-style-preload.js` **preload theo ý định người dùng**: khi chuột hover hoặc
   focus vào một link nội bộ → chèn `<link rel="preload" as="style">` cho CSS của route đó.
   Nhờ vậy CSS đã nằm trong cache đúng lúc điều hướng xảy ra → không còn FOUC.
4. Bổ sung 3 test chống hồi quy trong `FrontendAssetAndPerformanceTests`.

**Phát hiện phụ:** khi API chưa sẵn sàng, trang hiển thị thông báo sai
("Không tìm thấy thể loại phù hợp") thay vì lỗi kết nối. Server trong Docker chỉ khởi
động sau khi PostgreSQL `healthy` nên tình trạng này hiếm gặp hơn, nhưng vẫn nên xử lý
riêng cho thông báo lỗi.

### 5d. Triển khai Docker

| Thay đổi | Nội dung |
|----------|----------|
| `docker-compose.yml` | Thêm service `postgres` (image `postgres:17-alpine`, healthcheck, volume `postgres_data`); `server` phụ thuộc `postgres` theo điều kiện `service_healthy`; connection string mặc định trỏ `Host=postgres` |
| `src/Server/.../Dockerfile` | Cập nhật comment (SQLite → PostgreSQL) |
| `DEPLOY_DOCKER.md` | Thêm mục deploy PostgreSQL ngoài, mục thao tác với `psql` trong container, mục xử lý lỗi `Name or service not known` |

**Kết quả kiểm chứng trên stack Docker:**

| Hạng mục | Kết quả |
|----------|---------|
| `docker compose ps` | postgres `healthy`, server `Up`, client `Up` |
| Server startup | `Database schema verified in 2048 ms` → `Now listening on http://[::]:8080` |
| Schema trong container | 27 bảng, 40 phim (40 đã lập chỉ mục), 15 thể loại, 2 trigger, 3 migration |
| `GET /api/movies` | 40 phim |
| `GET /api/movies/search?q=xu cat` (không dấu) | 1 kết quả đúng |
| `GET /api/movies/search?q=Xứ Cát` (có dấu) | 1 kết quả đúng |
| Trang `/genres` (F5 lẫn điều hướng client-side) | 15 thẻ, `display: grid`, `border-radius: 18px` |
















#### Giữ nút "Đăng nhập" / "Đăng ký" ở mọi kích thước màn hình

Hai nút này **không được ẩn** — nhưng CSS đang ẩn chúng ở các màn hình hẹp:

| File | Breakpoint | Việc làm sai |
|------|-----------|-------------|
| `popup-polish.css` | ≤640px | `.nav-signup { display:none }` → **mất nút "Đăng ký"** |
| `mobile-ui-fixes.css` | ≤360px | `.nav-auth-actions { display:none }` → **mất cả hai nút** |

Sau khi sửa, cả hai nút luôn hiện, chỉ được thu gọn lại cho vừa thanh navbar:

| Breakpoint | Trước | Sau |
|------------|-------|-----|
| >640px | Đăng nhập 108×34 · Đăng ký 89×34 | **không đổi** |
| ≤640px | chỉ còn "Đăng nhập", cao 38px | **cả hai**, cao 34px, font .71rem, gap .3rem |
| ≤360px | mất cả hai | **cả hai**, cao 32px, font .68rem, gap .25rem |

Đã quét toàn bộ CSS: **không còn rule nào đặt `display:none` cho `.nav-signup` / `.nav-signin` /
`.nav-auth-actions`** ✓


#### Mỗi tập phim có ảnh nền riêng (trước đây mọi tập trùng ảnh bìa phim)

Danh sách tập trước đây hiển thị **cùng một ảnh bìa của phim** cho mọi tập nên nhìn như bị lặp.
Nay mỗi tập nhận một ảnh nền riêng, sinh động theo phim + số tập.

| | Trước | Sau |
|---|-------|-----|
| Nguồn ảnh | `/images/movies/movie-{id}-backdrop.webp` (giống nhau) | **`/api/episodes/{movieId}/{n}/thumbnail`** — mỗi tập một màu chuyển sắc riêng |
| Cách sinh | file tĩnh trong `wwwroot` | **SVG sinh động** ở `EpisodeThumbnailController`: gradient + vệt sáng xiên + số tập, 640×360, ~1,5 KB |
| Vì sao không tạo file | 1.120 file ảnh (40 phim × 28 tập) làm nặng repo | không cần file nào ✓ |

Sắc màu neo theo phim, dịch chuyển **23° mỗi tập** (tập 1 → 134°, tập 2 → 157°, tập 3 → 180° …) nên hai tập liền nhau luôn khác nhau.

**Ba lỗi phải xử lý khi triển khai (đều đã ghi chú trong mã):**

1. **URL phải là URL tuyệt đối.** Đường dẫn lưu trong CSDL là tương đối (`/api/episodes/…`) nhưng thẻ `<img>` của trình duyệt phân giải theo origin **của trang web**, không phải của API → ảnh không tải được. Server phải đổi thành URL tuyệt đối bằng `PublicUrlResolver` (cùng cơ chế dùng cho URL phát video).
2. **SVG phải hợp lệ XML.** Dấu nháy cuối chuỗi trước `"""` của raw string tạo ra 4 dấu nháy liền → thuộc tính `aria-label` mất dấu nháy → trình duyệt **tải được nhưng không decode được** (`naturalWidth = 0`).
3. **Cache trình duyệt giữ lại SVG lỗi.** Response cũ (SVG hỏng) vẫn nằm trong cache và được dùng lại cho URL không có query string. Thêm `?v=1` (cache-busting) và `Cache-Control: no-store` cho endpoint.

Ngoài ra ảnh tập bỏ `loading="lazy"`: ảnh do API sinh chưa biết kích thước nên trình duyệt trì hoãn tải trong khung cuộn, danh sách tập hiện trống ảnh.

**Kiểm chứng trên trang thật** (`/watch/33`): 28 ảnh · 28 URL khác nhau · **28/28 tải thành công** (640×360) · màu mỗi tập lệch 23° ✓

#### Bỏ thanh cuộn của cột phải

Sau khi tách "Danh sách tập" và "Phim tương tự" thành hai thẻ riêng, cột phải không còn lý do để
cuộn riêng nữa — thanh cuộn bên trong cột làm trang trông như bị nhốt trong một khung hẹp.

| | Trước | Sau |
|---|-------|-----|
| Cột phải | `position:sticky` + `max-height:calc(100dvh - 32px)` + `overflow-y:auto` | **trôi cùng trang** (`position:static`, `overflow:visible`) — không còn thanh cuộn |
| Độ cao cột | bị cắt ở chiều cao màn hình | nội dung đủ dài thì trang cuộn, cột không bị cắt |
| Thanh cuộn còn lại | 2 thanh (cột + khối danh sách tập) | **1 thanh duy nhất**, nằm trong khối danh sách tập (28 tập) |

> Nếu muốn bỏ luôn thanh cuộn trong khối danh sách tập thì bỏ `max-height` ở `.wv-episodes` —
> đổi lại khối này sẽ dài bằng số tập.

#### Thiết kế lại khối "phim tương tự" cho dễ nhìn (tối đa 5 phim mặc định)

Ở cột phụ (~300px) thẻ dọc 16:9 của trang thông tin phim bị nhỏ, tên phim nằm đè trên ảnh
nên khó đọc. Khối này chuyển sang **dạng hàng ngang** — cùng ngôn ngữ thị giác với danh sách tập
ngay phía trên:

```
[ảnh 96px]  Đại Chiến Titan: Hồi Kết Tuyệt Đối
            ★ 9.2   2023   Anime
```

| | Trước | Sau |
|---|-------|-----|
| Kiểu thẻ | dọc 16:9, tên đè gradient trên ảnh | **hàng ngang**: thumbnail 96px + tên + meta |
| Tên phim | trên ảnh, 0.72rem, 1 dòng | **dưới ảnh, 0.8rem, tối đa 2 dòng** |
| Thông tin | không có | **★ điểm · năm · định dạng** (Phim lẻ / Phim bộ / Anime) |
| Số phim mặc định | 1 | **5** (nút "Xem thêm 3 phim" mở đủ 8) |
| Khoảng cách giữa các hàng | — | 8px, hover nâng nhẹ + viền sáng |
| Focus | — | viền focus 2px rõ ràng |

Tách thành component dùng chung `RelatedMovieList` (hàng ngang) bên cạnh `RelatedMovieStrip`
(dải ngang 6 cột) — hai bố cục khác nhau về không gian nên tách riêng, cùng dùng chung một file
CSS `related-movies.css`.

**Tương phản đo được trên trang thật**: tiêu đề 15,9:1 · meta 6,8:1 · điểm ★ 10,0:1 ·
nút "Xem thêm" 8,3:1 — tất cả vượt WCAG AA ✓

#### Tách "Danh sách tập" và "Phim tương tự" thành hai thẻ riêng

Trước đây cột phải là **một thẻ duy nhất** chứa cả danh sách tập lẫn phim tương tự, nên hai
phần dính liền nhau và nhìn như một khối rối.

| | Trước | Sau |
|---|-------|-----|
| Cột phải (`.wv-rail`) | có nền, viền, bo góc, padding | **trơn** — chỉ là cột chứa các khối |
| Khối danh sách tập (`.wv-rail-episodes`) | dính trong thẻ chung | **thẻ riêng**: nền `#15171c`, viền 1px, bo góc 12px, cao 474px, cuộn nội bộ |
| Khối phim tương tự (`.wv-rail-similar`) | dính trong thẻ chung | **thẻ riêng**: cùng kiểu, cao 246px |
| Khoảng cách giữa hai khối | 0 (chung khung) | **16px** |

```css
.wv-rail { display:flex; flex-direction:column; gap:16px; position:sticky; top:16px; max-height:calc(100dvh - 32px); overflow-y:auto; }
.wv-rail-block { background:#15171c; border:1px solid var(--wv-line); border-radius:12px; padding:16px 14px; }
```

Danh sách tập giữ thanh cuộn riêng bên trong thẻ (chiều cao tối đa
`clamp(300px,34vw,610px)`) nên khối không bị kéo dài; phần "phim tương tự" nằm ngay dưới và
truy cập được bằng thanh cuộn của cột.

#### Dải "phim tương tự" chỉ chiếm **một dòng**

Mặc định cột phải chỉ hiện **2 phim (1 dòng)** cho khối "phim tương tự" — chiều cao cả khối
chỉ 96px nên danh sách tập vẫn thoải mái. Phần còn lại ẩn sau nút **"Xem thêm 6 phim"**; bấm vào
mở đủ 8 phim (4 dòng) và nút đổi thành **"Thu gọn"** (`aria-expanded` theo trạng thái).

### 5n. Tái cấu trúc trang xem: hai nửa theo chiều dọc, tỉ lệ 3:1

Theo bản vẽ: trang xem chia làm **hai nửa theo chiều dọc, tỉ lệ 3:1** — nửa lớn chứa toàn bộ
nội dung, nửa hẹp chứa danh sách tập và phim tương tự.

```
┌──────────────────────────────┬───────────────┐
│  khung phát + thanh nguồn    │  DANH SÁCH TẬP│
│                              │  (1 dòng 1 tập)│
├──────────────────────────────┤               │
│  Thông tin phim              │  PHIM TƯƠNG TỰ│
│  Thảo luận                   │  (2 phim 1 dòng)│
└──────────────────────────────┴───────────────┘
        3 phần                        1 phần
```

#### Thay đổi cấu trúc

| | Trước | Sau |
|---|-------|-----|
| Lưới | `minmax(0,2.8fr) minmax(290px,1fr)` | **`minmax(0,3fr) minmax(280px,1fr)`** |
| Vùng | 2 vùng: `wv-main` (khung phát) + `wv-rail` | **3 vùng đặt tên**: `player` / `content` / `rail` |
| "Thông tin phim" + "Thảo luận" | nằm **ngoài lưới**, trải hết bề ngang (937px) | nằm trong **cột trái (3 phần)**, ngay dưới khung phát |
| Cột phải | cao bằng nội dung, cuộn theo trang | **dánh theo** (`position: sticky`) nên danh sách tập luôn trong tầm nhìn khi đọc thông tin/thảo luận |
| Thứ tự DOM | — | giữ nguyên (khung phát → danh sách tập → nội dung) nhờ `grid-template-areas`, nên vị trí hiển thị đúng mà thứ tự đọc vẫn hợp lý |

Khai báo vùng:
```css
grid-template-areas: "player rail" "content rail";
grid-template-rows: auto 1fr;
```

#### Responsive

Ở ≤900px lưới về một cột và **phải reset luôn `grid-template-areas`** (nếu không, vùng `rail` vẫn bị
đặt ở cột thứ hai dù lưới chỉ còn một cột):
```css
grid-template-columns: 1fr;
grid-template-areas: "player" "content" "rail";
```
Cột phải cũng bỏ `sticky` và bỏ giới hạn chiều cao ở chế độ này.

#### Kiểm chứng

| Mục | Kết quả |
|-----|---------|
| Tỉ lệ hai cột (viewport 1301px) | 889,5px / 296,5px = **3,00 : 1** ✓ |
| Tỉ lệ (container 1360px) | 1005px / 335px = **3,00 : 1** ✓ |
| Vùng lưới | `"player rail" / "content rail"` ✓ |
| Danh sách tập | 28 tập, **1 dòng 1 tập** ✓ |
| Phim tương tự | 4 thẻ, **2 phim 1 dòng** (143px/thẻ) ✓ |
| Cột phải | nằm bên phải, `sticky` ✓ |
| Tràn ngang | không ✓ |

> Lưu ý: cột phải giữ một bề rộng tối thiểu 280px để danh sách tập (thumbnail 112px + phần chữ)
> còn đọc được. Khi bề rộng đủ lớn (≥ ~1240px) tỉ lệ ra đúng 3:1; ở màn hẹp hơn thì cột phải bám
> mốc tối thiểu thay vì bóp nát danh sách tập.

### 5m. Tách khối danh sách tập / phim tương tự + nút "Xem thêm" + sửa dữ liệu tập thiếu

Yêu cầu: **tách riêng 2 khối** (danh sách tập và phim tương tự), **danh sách tập 1 dòng 1 tập**,
**phim tương tự 1 dòng 2 phim**, và **thêm nút "Xem thêm"** để xem nhiều phim hơn.

#### Giao diện cột phải của trang xem

| | Trước | Sau |
|---|-------|-----|
| Cấu trúc | 1 khối `aside.wv-rail` chứa tất cả | **2 khối rõ ràng**: `.wv-rail-episodes` (danh sách tập) và `.wv-rail-similar` (phim tương tự), ngăn cách bằng đường kẻ |
| Danh sách tập | 1 dòng 1 tập | giữ nguyên **1 dòng 1 tập** ✓ (đã xác minh: 28 tập nằm trên 28 dòng) |
| Phim tương tự | 2 phim 1 dòng | giữ **2 phim 1 dòng** ✓ (thẻ 125×77px) |
| Số phim hiện ra | 4 cố định | 4, bấm **"Xem thêm 4 phim"** → hiện đủ 8, nút đổi thành **"Thu gọn"** (`aria-expanded` cập nhật theo trạng thái) |

Nút "Xem thêm" dùng nút ẩn/hiện dạng text kèm mũi tên xoay, đặt trong CSS chung
`related-movies.css` vì thẻ do component `RelatedMovieStrip` sinh ra (CSS scoped của trang không áp dụng
cho phần tử của component khác).

#### Sửa lỗi dữ liệu: danh mục khai 28 tập nhưng chỉ tạo 1 tập

Trong lúc kiểm tra "1 dòng 1 tập" mới phát hiện: **phim Frieren ghi "28 tập" trên thẻ nhưng danh
sách tập chỉ có 1 mục** — người xem không chọn được tập nào khác. Nguyên nhân nằm ở dữ liệu seed:
`TotalEpisodes` khai 28 nhưng `BuildEpisodes` chỉ sinh 1 tập cho mỗi tiêu đề được liệt kê.

| | Trước | Sau |
|---|-------|-----|
| Frieren | 1 / 28 tập | **28 / 28** |
| Arcane | 3 / 9 tập | **9 / 9** |
| Solo Leveling | 2 / 12 tập | **12 / 12** |
| Shogun | 2 / 10 tập | **10 / 10** |

Cách sửa gồm hai phần:

1. `EnsureEpisodeCount` bù đủ số tập ngay trong danh mục ⇒ CSDL mới tạo ra đã khớp.
2. `RepairMissingEpisodesAsync` chạy **mỗi lần khởi động** (cùng chỗ với bước sửa stream cũ) ⇒
   CSDL đang chạy cũng được vá.

**Hai bẫy đã gặp khi sửa (đều đã ghi chú trong mã):**

- **Khoá ngoại**: gắn sẵn `Streams` vào entity tập thì EF `INSERT` bảng con `stream_sources` **trước**
  bảng chính `episodes` ⇒ `FK_stream_sources_episodes_EpisodeId` vi phạm. Phải tạo tập, lưu, rồi mới
  ghi stream.
- **Số thứ tự nhảy cách**: thêm tập vào cả `context.Episodes` lẫn `movie.Episodes` thì EF fixup thêm
  nó vào navigation **hai lần** ⇒ `Count` tăng 2 ⇒ sinh ra các số 1, 2, 4, 6, 8… Bù theo **số thứ tự
  tập** (1…N) thay vì theo số lượng hiện có vừa đúng vừa idempotent — chạy lại nhiều lần cũng cho
  cùng kết quả.

### 5l. Làm gọn dải "phim tương tự" và dùng chung thẻ với trang phim

Sau khi thêm dải "phim tương tự" ở mục 5k, người dùng yêu cầu: **gọn lại**, **đặt dưới danh sách
tập**, và **dùng đúng kiểu thẻ như dải "phim tương tự" ở trang thông tin phim**.

#### Tách thành component dùng chung

Trước đây thẻ "phim tương tự" nằm trong `MovieDetail.razor.css` — là CSS **scoped**, nên trang xem
không dùng lại được, và nếu copy sang sẽ tạo hai nơi cùng sửa một kiểu thẻ.

| | Trước | Sau |
|---|-------|-----|
| Markup | viết lặp ở 2 trang | 1 component `RelatedMovieStrip` dùng chung |
| CSS | scoped trong `MovieDetail.razor.css` | 1 file chung `wwwroot/css/related-movies.css` |
| Đường dẫn | viết cứng `/movie/{id}` | tham số `HrefTemplate` (`/movie/{0}` hoặc `/watch/{0}`) |
| Tiêu đề / nút phụ | viết cứng | tham số `Title`, `HeadingId`, `Action` (RenderFragment) |

#### Vị trí & độ gọn trên trang xem

| | Trước | Sau |
|---|-------|-----|
| Vị trí | section lớn giữa cột chính, dưới "Thông tin phim" | **trong cột phải, ngay dưới danh sách tập** |
| Số phim | 8 | **4** (tiết kiệm chiều cao cột phải) |
| Kiểu thẻ | `MovieCard` (poster 2:3) | **thẻ của trang phim** (ảnh 16:9 + tên đè gradient) |
| Kích thước | lưới 4 cột, cao ~530px/thẻ | 2 cột, thẻ 136×77px — **cả dải chỉ cao 211px** |
| Link điều hướng trang | thêm mục "Phim tương tự" | bỏ (dải đã nằm trong cột phải) |

Lưới của component tự lấp đầy bề rộng (`auto-fill, minmax(180px, 1fr)`) nên cùng một component cho
6 cột trên trang phim và 2 cột trong cột phải của trang xem mà không cần viết layout riêng.

#### Một bẫy kỹ thuật đã gặp

Bản đầu tiên đặt style của biến thể trang xem trong `Watch.razor.css` — **CSS scoped**. Nhưng thẻ do
component `RelatedMovieStrip` sinh ra nên **không mang attribute `b-xxx`** của trang xem ⇒ rule
`.wv-similar-track[b-…]` không khớp ⇒ lưới rơi về 1 cột (6 phim xếp dọc, dài lê thê).
Cách sửa: các rule riêng của biến thể trang xem phải nằm ở **CSS chung** (vì phần tử do component
khác render), không phải CSS scoped của trang.

### 5k. Thêm mục "Phim tương tự" trên trang xem phim

**Yêu cầu**: người xem không muốn xem hết phim rồi phải rời trang đi tìm phim mới — cần có
ngay trên trang xem một danh sách phim tương tự, bấm là xem tiếp.

#### Nguồn dữ liệu

Server **đã có sẵn** endpoint `GET /api/movies/{id}/similar` và `MovieDetailDto.SimilarMovies`
được điền sẵn trong response chi tiết phim → **không cần thêm request mạng nào** khi mở trang
xem. Chỉ cần render ra và cải thiện thuật toán xếp hạng.

#### Thuật toán xếp hạng (đã cải thiện)

| | Trước | Sau |
|---|-------|-----|
| Điều kiện | cùng định dạng **hoặc** chung thể loại | như trước **+ bắt buộc đã có tập phát** |
| Xếp hạng | theo điểm đánh giá | **theo số thể loại chung** → rồi điểm đánh giá → rồi lượt xem |
| Số lượng | 6 | **8** (dùng chung cho trang xem và trang thông tin phim) |

**Vì sao đổi thứ tự xếp hạng**: xếp theo điểm đánh giá thì phim hay nhất thắng chứ không phải
phim giống nhất — ví dụ "Xứ Cát" (Hành Động/Khoa Học Viễn Tưởng) lại gợi ý Breaking Bad
(Chính Lịch/Hoạt Hình) chỉ vì điểm cao. Sau khi đổi, đầu danh sách là
"The Last of Us" · "Người Nhện" · "Kẻ Trừm Giấc Mơ" — đều chung 3 thể loại ✓.

**Vì sao thêm điều kiện "đã có tập phát"**: nếu gợi ý một phim chưa có nguồn, người xem bấm vào sẽ
gặp ngay màn hình *"chưa có nguồn phát"* — tệ hơn là không gợi ý.

#### Giao diện

- Vị trí: sau mục **Thông tin phim**, trước **Thảo luận**; thêm link tương ứng trong thanh
  điều hướng nội dung trang.
- Dùng lại đúng component `MovieCard` của app (cùng poster, cùng kiểu tên/điểm/năm) → không
  tạo thêm một kiểu thẻ mới.
- `MovieCard` nhận thêm tham số `Href` (mặc định `/movie/{id}`) để trang xem trỏ thẳng tới
  `/watch/{id}` — bấm là **xem ngay**, không qua trang chi tiết rồi mới bấm "Xem".
- Lưới 4 / 3 / 2 cột theo bề rộng (≥1200px / ≥900px / <900px), có focus ring riêng, không tràn
  ngang.

#### Kiểm chứng

8 thẻ hiển thị đúng, ảnh bìa tải thành công, không tràn ngang, link trỏ đúng `/watch/{id}`,
cả 8 phim đều có tập + stream nên bấm là xem được ngay.

### 5j. Lỗi "Media load rejected by URL safety check" — CSP chặn video

**Triệu chứng**: trang `/watch/{id}` hiện *"Không thể phát video — Nguồn phát không phản hồi hoặc
định dạng video không được trình duyệt hỗ trợ"*, mã lỗi `MEDIA_ERR_4`.

**Nguyên nhân**: URL video **hoàn toàn hợp lệ**, nhưng **chính sách CSP của trang chặn** nó:

```
Loading media from 'http://localhost:5085/api/stream/play?episodeId=101&pos=8&exp=...&sig=...'
violates the following Content Security Policy directive: "media-src 'self' https: blob:".
```

Video được nạp từ API ở **origin khác** (`http://localhost:5085` so với trang `http://localhost:5229`).
Chính sách cũ chỉ cho phép `'self'` + `https:` + `blob:` → ở chế độ phát triển (API chạy http ở
cổng khác) mọi request video đều bị chặn ⇒ trình duyệt báo lỗi an toàn, người xem chỉ thấy
màn hình đen.

> Vì sao harness kiểm tra lại chạy được? Harness là trang tĩnh **không có CSP** nên video nạp
> bình thường — đó là lý do lỗi chỉ lộ ra trên trang thật.

**Sửa**: cho phép `http:` trong `media-src` và `img-src` **chỉ ở chế độ phát triển**, đúng pattern
mà `connect-src` đã dùng từ trước. Ở production API dùng https nên chính sách giữ nguyên độ chặt
(`https:` đã bao phủ).

| | Trước | Sau (development) | Sau (production) |
|---|-------|--------------------|------------------|
| `media-src` | `'self' https: blob:` | `'self' https: blob: http:` | `'self' https: blob:` (không đổi) |
| `img-src` | `'self' data: https:` | `'self' data: https: http:` | `'self' data: https:` (không đổi) |

**Kiểm chứng trên trang thật**: `readyState = 4` (đủ dữ liệu để phát), `duration = 183s`,
không có `MediaError`, **không còn bản ghi CSP violation nào** trong console.

### 5i. Đóng gói lại luồng phát video: proxy qua server + watermark canvas

Sau khi rà soát xong (mục 5h), hai điểm còn lại được xử lý triệt để: **URL CDN gốc bị lộ ra
trình duyệt** và **watermark chống chia sẻ gần như vô hiệu**.

#### 1. Video được proxy qua server thay vì chuyển hướng (Redirect)

Trước đây `GET /api/stream/play` trả `302` tới URL CDN gốc. Hệ quả:

- URL thật lộ ra trong header `Location` và trong tab Network → dùng lại **vô thời hạn**, bỏ qua
  được toàn bộ cơ chế ký HMAC, giới hạn thời hạn (TTL) và ràng buộc IP.
- Toàn bộ phân tích HLS sau đó tải thẳng từ CDN, bypass hoàn toàn máy chủ.

Nay nội dung được **proxy** qua máy chủ:

| Mục | Trước | Sau |
|-----|-------|-----|
| Phản hồi | `302` + header `Location` chứa URL CDN | `200`/`206` + **nội dung video**, không có `Location` |
| URL CDN gốc | lộ ra cho trình duyệt | **không bao giờ** rời khỏi máy chủ |
| Tua (seek) | trình duyệt tự xử lý | header `Range` được chuyển tiếp lên máy chủ nguồn → **vẫn tua được** |
| Bộ nhớ đệm | không kiểm soát | `Cache-Control: no-store, private` + `Pragma: no-cache` + `Referrer-Policy: no-referrer` |
| Tải | trình duyệt tải thẳng CDN | `HttpCompletionOption.ResponseHeadersRead` → chuyển tiếp theo luồng, không gom vào RAM |

> **Hai lỗi đã mắc phải khi làm và đã sửa** (đều khiến video không phát):
> 1. `Content-Type` phải đặt qua `Response.ContentType`, không đặt trong `Response.Headers` — nếu không
>    trình duyệt báo *"no supported source was found"*.
> 2. `Content-Type` / `Content-Length` / `Content-Range` / `Accept-Ranges` nằm ở
>    `HttpResponseMessage.Content.Headers`, **không phải** `HttpResponseMessage.Headers`.

**Kiểm chứng trong trình duyệt**: `duration=46.6s · 960x400 → canplay → playing → timeupdate chạy
liên tục → seek tới giây 20 thành công`, không có lỗi.

**Đánh đổi đã biết**: băng thông video giờ đi qua máy chủ API thay vì đi thẳng CDN. Với hạ
tầng một tiến trình thì đây là đánh đổi có chủ đích để bảo vệ nội dung; khi mở rộng quy mô
nên tách luồng video ra service riêng (CDN trước, proxy sau).

#### 2. Watermark chống chia sẻ vẽ trên canvas

| | Trước | Sau |
|---|-------|-----|
| Hình thức | `<div>` + `<span>` text trong DOM | **`<canvas>`** phủ lên video, JS vẽ |
| Gỡ bằng DevTools | xoá 1 node là mất hoàn toàn | phải tìm đúng canvas **và** dừng cả vòng vẽ lại |
| Độ nhạy cảm | `opacity 0.22` × `rgba(255,255,255,0.45)` ⇒ alpha hiệu dụng **≈0.1** (gần như vô hình) | chữ trắng 50% **+ bóng đen 38%** ⇒ đọc được trên cả nền sáng lẫn nền tối của video |
| Vị trí | 6 vùng cố định, đổi mỗi 30 giây (do timer .NET re-render cả component) | 6 vùng an toàn **vẽ trên canvas**, đổi mỗi 30 giây + xoay nhẹ ngẫu nhiên |
| PII | hiện **email đầy đủ** trong khung hình | email **che bớt** (`q***@cinemaflow.vn`) + mốc thời gian |

#### 3. Test được cập nhật theo hành vi mới (không làm yếu assertion)

| Test | Thay đổi |
|------|----------|
| `Integration_PlayStream_WithValidSignature_Returns302Redirect` | Đổi tên thành `..._ProxiesVideoWithoutLeakingUpstreamUrl` và **siết chặt hơn**: phải **không** có 302/`Location`, **không** header nào chứa "cdn" hoặc ".mp4", nội dung phải là video (không phải HTML lỗi) |
| `CinemaPlayerContainsDynamicWatermarkOverlayAndSafeZones` | Thay assertion "có `cp-watermark-text`" bằng assertion **mạnh hơn**: có `cp-watermark-canvas`, `data-watermark`, `data-watermark-zone` **và** file JS phải có `setupWatermark` + `getContext('2d')` |
| `CinemaPlayerWatermarkShiftsPositionsAcrossSixSafeZones` | Giữ nguyên (API xoay 6 vùng vẫn còn) |

### 5h. Rà soát trình phát video (trang `/watch/{id}`)

Rà soát gồm **đo trực tiếp trên DOM thật** (mọi phần tử chữ/nút/slider, contrast, cỡ chữ, tên
truy cập) và **đọc toàn bộ mã nguồn** `CinemaPlayer.razor` + `cinema-player.js` +
`CinemaPlayer.razor.css` + luồng ký URL phía server.

#### Lỗi chặn video — không bao giờ phát được

| # | Lỗi | Nguyên nhân | Sửa |
|---|-----|-------------|-----|
| P1 | **URL ký trỏ tới tập 0** → `GET /api/stream/play?episodeId=0` trả **404** → video không bao giờ phát | `DatabaseMovieService.MapDetail` chiếu sang `StreamSourceDto` mà **quên map `EpisodeId` và `Position`** (chúng giữ giá trị 0), trong khi hai trường này chính là tham số ký URL | Map đủ `Id`, `EpisodeId`, `Position` |
| P2 | **Browser không phân giải được host** → `MEDIA_ELEMENT_ERROR: Media load rejected by URL safety check` | URL ký dựng từ `{Request.Scheme}://{Request.Host}`. Trong Docker, `Request.Host` là tên service nội bộ (`server:8080`) — trình duyệt ngoài container không mở được | Thêm `PublicUrlResolver` + cấu hình `Security:PublicApiBaseUrl` (mặc định `http://localhost:5085` trong compose). 3 controller dùng resolver này |
| P3 | **Lỗi không hiện cho người dùng** → màn hình đen, không có thông báo | Sự kiện `error` của thẻ `<video>` xảy ra *trước khi circuit Blazor kết nối* nên `@onerror` bị rơi mất; JS chỉ tắt spinner | JS báo lỗi qua `OnMediaError` (JS interop) → hiện panel lỗi với mã lỗi + nút **Thử lại** và **Dùng nguồn khác** |

> **Bài học rút ra trong lúc sửa:** bản vá "luôn kiểm tra quyền VIP ở endpoint phát" (đề xuất của
> rà soát tĩnh) **làm hỏng hoàn toàn** khả năng phát video — thẻ `<video>` không gửi kèm Bearer
> token nên mọi request đều bị 403. Chữ ký trong URL **chính là** giấy phép (gắn IP + có thời
> hạn, chỉ cấp cho tài khoản VIP). Phải hoàn nguyên và ghi chú lại để không ai sửa lại.

**Kiểm chứng end-to-end trong trình duyệt:**
`loadedmetadata: duration=46.6s size=960x400` → `canplay (readyState 3)` → `playing` →
`timeupdate` chạy → không có lỗi.

#### Bảo mật

| # | Vấn đề | Sửa |
|---|--------|-----|
| P4 | **Nguồn không VIP được trả URL CDN gốc, chưa ký** → ai cũng tải trực tiếp được, cơ chế ký và giới hạn thời hạn mất tác dụng | Ký **mọi** nguồn trước khi đưa ra; `StreamUrl` rỗng chỉ dành cho nguồn VIP khi người dùng chưa có VIP |
| P5 | **URL đã ký rò rỉ ra console** — handler lỗi HLS log nguyên đối tượng `data` của hls.js, mà `data.url` chính là URL stream đã ký | Chỉ log metadata an toàn (`type`, `details`), không dump `data` |
| P6 | TTL mặc định 120 phút — URL ký dùng lại được rất lâu | TTL mặc định **30 phút** (cấu hình được qua `Security:StreamTtlMinutes`) |

#### Trạng thái & điều khiển

| # | Vấn đề | Sửa |
|---|--------|-----|
| P7 | **Vòng lặp retry HLS vô hạn** — lỗi mạng fatal gọi `hls.startLoad()` ngay trong handler → lỗi lặp lại → busy-loop, kéo CPU và spam console | Bộ đếm + backoff (1s → 2s → 4s → 8s), sau 3 lần (mạng) / 2 lần (media) thì dừng và báo lỗi cho UI; thêm `hls.destroyed` guard |
| P8 | `requestFullscreen()` trả Promise bị bỏ qua → nút toàn màn hình **im lặng không làm gì** trên iOS | Bắt lỗi Promise → thông báo "Trình duyệt này không hỗ trợ toàn màn hình cho khung phim"; iOS dùng `videoEl.webkitEnterFullscreen()` |
| P9 | Không theo dõi `fullscreenchange` → icon/nhãn toàn màn hình không đổi | Nghe `fullscreenchange`, cập nhật `aria-label` + `aria-pressed` + reset bộ đếm ẩn controls |
| P10 | Thông báo lỗi nói "thử nguồn khác" nhưng chỉ có nút "Thử lại" (nạp lại đúng nguồn đang lỗi) | Thêm nút **Dùng nguồn khác** (tự chuyển sang nguồn kế tiếp) + thông điệp phân biệt lỗi mạng / giải mã / định dạng không hỗ trợ |
| P11 | Menu chất lượng không đóng bằng ESC, không đóng khi click ra ngoài, mất focus sau khi chọn (mất luôn phím tắt của player) | ESC + lớp phủ trong suốt đóng menu khi click ra ngoài + trả focus về nút đã mở |
| P12 | Mức âm lượng không được nhớ giữa các lần mở trang; bấm "bật tiếng" khi volume = 0 vẫn không nghe thấy gì | Lưu/khôi phục `localStorage` + nhớ `lastNonZeroVolume` |
| P13 | Video bị tải **2 lần** (Razor render `src` rồi JS lại `set src`) | JS chỉ gán `src` khi khác URL hiện tại |

#### A11y

| # | Vấn đề | Sửa |
|---|--------|-----|
| P14 | **`aria-expanded="@isQualityMenuOpen"` render sai** — Blazor render bool thành thuộc tính rỗng khi `true` và **bỏ hẳn** khi `false` (đã xác minh bằng render thật .NET 10) | Dùng `@(isQualityMenuOpen ? "true" : "false")` |
| P15 | 6 nút icon-only chỉ có `title`, không có `aria-label`; nhãn play/pause và tắt tiếng không phản ánh trạng thái | Thêm `aria-label` cho mọi nút; `aria-pressed` động cho play/pause, tắt tiếng, PiP, toàn màn hình |
| P16 | Thanh tiến trình `role="slider"` không có `aria-valuetext` → trình đọc màn hình đọc "35 phần trăm" thay vì "23 giây / 45 phút" | JS cập nhật `aria-valuetext` theo thời gian thật |
| P17 | Spinner đệm có `role="status"` **và** `aria-hidden="true"` → trình đọc màn hình không hề biết video đang tải | JS bật/tắt `aria-hidden` theo trạng thái đệm |
| P18 | Nút lỗi dùng `role="status"` (thông báo nhẹ) thay vì `role="alert"` | Đổi sang `role="alert"` |

#### Responsive

| # | Vấn đề | Sửa |
|---|--------|-----|
| P19 | Thanh controls **không tính `safe-area-inset`** → trên iPhone ở chế độ ngang, nút Play/Fullscreen nằm dưới home indicator, không bấm được | `padding-bottom/left/right` cộng `env(safe-area-inset-*)` |
| P20 | Ở màn ≤600px **nút tua ±10 giây bị ẩn** → mất hoàn toàn khả năng tua trên điện thoại | Giữ nút tua (thu nhỏ 32px), chỉ ẩn PiP và thanh âm lượng |
| P21 | Màn hình thấp (điện thoại nằm ngang) khung phim cao hơn viewport → thanh controls nằm ngoài màn hình | `@media (orientation: landscape) and (max-height: 500px)` → khung phim `height: 100dvh` |

### 5g. Sửa lỗi "lần đầu vào trang" của khu vực Admin

Người dùng báo: **vừa đăng nhập xong bấm vào trang admin thì bị lỗi hiển thị**. Truy ra ba
nguyên nhân độc lập, đều nằm ở đường đi vào khu vực quản trị.

#### 1. Phiên đăng nhập bị xoá khi API chưa sẵn sàng

`AuthState.RestoreAsync()` gọi `GetMeAsync()`, mà `AccountApiClient.SendAsync()` **nuốt mọi
lỗi và trả `null`**. Kết quả: lúc API chưa bật (stack còn đang khởi động), `null` bị hiểu
nhầm là "token không hợp lệ" → `LogoutAsync()` **xoá sạch phiên** → người dùng bị đá ra
trang đăng nhập, phải đăng nhập lại.

| | Trước | Sau |
|---|-------|-----|
| API chưa phản hồi | `null` ⇒ xoá phiên ⇒ đá ra `/login` | `null` ⇒ giữ phiên, bật cờ `ApiUnavailable` |
| API trả 401 | `null` ⇒ xoá phiên | envelope `Success = false` ⇒ xoá phiên |

Thêm `IAccountApiClient.GetMeResultAsync()` trả nguyên envelope của API để phân biệt hai
trạng thái, `AuthState.ApiUnavailable` để giao diện biết API đang chưa sẵn sàng, và
`AdminLayout` hiện màn hình *"Máy chủ chưa sẵn sàng — đang kết nối lại"* kèm nút **Thử lại
ngay** + tự thử lại mỗi 2 giây (30 lần) thay vì đá người dùng ra trang đăng nhập.
`CheckAccess()` cũng bỏ qua việc điều hướng khi `ApiUnavailable` là true.

#### 2. Nhảy flash CSS khi điều hướng từ trang đăng nhập

Bảng kích hoạt CSS của cockpit nằm trong `HeadContent` của `AdminLayout` (98 KB). Sau khi
đăng nhập, `Nav.NavigateTo("/admin")` điều hướng **client-side** ⇒ nội dung cockpit được
vẽ **trước khi** bảng kích hoạt kịp tải về ⇒ một nhịp "chữ bay" rồi mới đóng khung.

- `Login.razor`: khi tài khoản là quản trị và đích đến nằm trong `/admin` thì dùng
  `NavigateTo(..., forceLoad: true)` ⇒ tải lại toàn bộ tài liệu, SSR đưa sẵn bảng kích hoạt
  vào `<head>` ⇒ không còn nhịp giật.
- `App.razor`: khi đã có phiên (`Auth.HasSession`) thì `preload` bảng kích hoạt admin ở chế
  độ nền, nên các lần điều hướng client-side về `/admin` sau này cũng không giật. Khách
  chưa đăng nhập không phải tải bảng này.

#### 3. Tab trình duyệt không có tiêu đề ở lần tải đầu

Trang admin chỉ có tiêu đề khi đã render component trang (`CinemaPageTitle`). Lúc SSR ở
cổng chặn truy cập thì `<head>` **không có `<title>`** ⇒ tab trình duyệt hiện đường dẫn URL.
Bổ sung tiêu đề dự phòng cho từng trạng thái của cổng chặn.

> Lưu ý kỹ thuật: Blazor chỉ nhận **một** khối `HeadContent` cho mỗi component. Khi tách
> tiêu đề vào trong nhánh `@if`, khối `HeadContent` bên ngoài bị ghi đè và bảng kích hoạt
> admin biến mất khỏi SSR. Vì vậy tiêu đề dự phòng được gộp vào **cùng một** khối
> `HeadContent` với thẻ `<link>` của bảng kích hoạt.

#### Kiểm chứng

| Hạng mục | Trước | Sau |
|----------|-------|-----|
| SSR `/admin` có `<title>` | không | **có** (đúng 1 thẻ) |
| SSR `/admin` có bảng kích hoạt admin | có | **có** (sau khi gộp `HeadContent`) |
| API chưa bật khi mở trang | xoá phiên, đá ra `/login` | **giữ phiên**, tự thử lại |
| Đăng nhập rồi vào `/admin` | nhảy flash CSS | **tải lại tài liệu, không giật** |
| Test hồi quy | — | 2 test mới: `UnreachableApiKeepsSessionAndFlagsUnavailable`, `RefreshClearsUnavailableFlagWhenApiAnswers` |

### 5f. Rà soát UI/UX giao diện Admin (đặc biệt lỗi hiển thị chữ)

Rà soát bằng cách **đo trực tiếp trên DOM thật** của cả 7 trang admin ở cả hai chế độ sáng/tối:
mỗi phần tử chữ được đo `color` / `background` đã resolve (bỏ qua màu nền bị gradient che) và
tính tỉ lệ tương phản WCAG, kèm cỡ chữ, `text-overflow`, tooltip và font thực tế.

#### Lỗi nghiêm trọng nhất: chữ hiển thị **ciphertext mã hoá**

Header cockpit và các bảng (Người dùng, Đánh giá, Gói VIP) hiển thị chuỗi kiểu
`CfDJ8Lz-AydO7lFJvtbSw85QWiWTr_uATHrBLEcxzMXywbGOfQxykIYhwF0lYkQHnchM-...` thay vì tên/email.

| Mắt xích | Nguyên nhân | Cách sửa |
|----------|-------------|----------|
| Khóa mã hoá trong `%TEMP%` | `FieldEncryptor.Current` **chưa bao giờ được gán**, nên nó tự tạo key ring riêng ở thư mục tạm của tiến trình — mất hoàn toàn mỗi lần rebuild container, trong khi key ring của app đã được mount volume `/app/keys` | Gán `FieldEncryptor.Current` từ `IDataProtectionProvider` của app ngay sau `builder.Build()` |
| Ciphertext lọt ra UI | `Decrypt()` nuốt lỗi và **trả nguyên ciphertext** khi giải mã thất bại | `Decrypt()` trả `null` cho giá trị mang header Data Protection (`CfDJ8`) — UI hiện "—" thay vì chuỗi rác |
| Dữ liệu demo bị "chết" | Seeder chỉ chạy khi bảng `users` còn trống, nên dữ liệu mã hoá cũ không bao giờ được làm lại | Thêm `RepairUnreadableDemoIdentitiesAsync()` khôi phục danh tính demo và mã hoá lại bằng key hiện tại |

#### Tương phản: light mode hỏng nặng, dark mode sạch

Cockpit được dựng cho nền tối nhưng khi bật light mode thì **nền đổi trắng còn màu chữ accent
vẫn là bản màu nền tối**:

| Nhóm | Trước (light) | Sau |
|------|---------------|-----|
| `.cmd-text` ("Tìm nhanh..."), `.cmd-shortcut` ("Ctrl+K") | **1,00 : 1** (gần như vô hình) | 5,9 : 1 |
| `.chart-stat-badge`, `.infra-status-chip`, `.status-pill.status-green` | 1,67 – 1,92 | ≥ 5,4 |
| `.rating-cell`, `.rating-score-val`, `.quality-badge-gold`, `.badge-tag-tier.vip`, `.kpi-change` | 1,67 – 2,15 | ≥ 4,8 |
| `.telemetry-badge`, `.status-pill.status-blue/purple` | 2,54 – 2,64 | ≥ 4,8 |
| `.utility-separator` | 2,56 | 4,76 |
| `.filter-count-chip`, `.view-mode-btn` | 4,34 | 7,5 |
| **Tổng lỗi đo được trên 7 trang** | **30** | **0** |

Cách sửa mang tính hệ thống thay vì vá từng chỗ:

- Thêm hệ token `--admin-text-strong/body/muted/faint` và `--admin-accent-*` trong
  `.admin-workspace-shell`, tự đảo giá trị theo `[data-theme="light"]`.
- **98 khai báo `color: rgba(255,255,255,x)`** trong `admin-workspace.css` chuyển sang token
  → toàn bộ chữ trắng cũ tự đổi sang nền sáng thay vì biến mất.
- 69 màu hardcode trong `style="..."` của 7 file `.razor` admin chuyển sang token.
- Các rule `!important` của light theme (`.filter-count-chip`, `.view-mode-btn`,
  `.live-status-pill`, `.utility-separator`) sửa trực tiếp trong `light-theme.css`.

#### Chữ nhỏ hơn 11px

`.brand-tag` (9,9px), `.filter-label`, `.telemetry-badge`, `.donut-center-lbl`,
`.cmd-shortcut`, `.user-mini-avatar` (10,9px) → nâng lên ≥ 11,5px. Trong cockpit, dưới 11px
chữ không còn đọc được trên màn hình 1×.

#### Rule `.kpi-change` không tồn tại

Các thẻ KPI dùng `<span class="kpi-change">` (không có modifier) mà CSS **chỉ định nghĩa
`.kpi-change.up` và `.kpi-change.warn`** → chúng kế thừa màu của `.kpi-body` và tương phản
chỉ 2,15:1 ở light mode. Đã bổ sung rule gốc.

#### Số liệu sai trên giao diện

| Trang | Trước | Sau |
|------|-------|-----|
| `/admin/movies` | "Hiển thị: **10 / 0** phim", tab định dạng đếm 0 | "10 / 40", tab 40 / 19 / 11 / 10 |
| `/admin/episodes` | "Hiển thị: **0 / 0** tập phim" | "12 / 12" |
| `/admin/vip` | "Tổng: **0** giao dịch", KPI doanh thu "—" | "5 giao dịch", doanh thu 707.000 ₫ |

Nguyên nhân: sau khi chuyển sang phân trang server-side, các biến cũ (`allMoviesList`,
`episodesList`) không còn được nạp nhưng markup vẫn tham chiếu; đồng thời số đếm theo tab
phải lấy từ API dashboard (`TotalMovies`/`TotalSeries`/`TotalAnime`, `TotalItems`) thay vì
đếm trên trang hiện tại. Tab "Sắp hết hạn" cũng chuyển sang số liệu server
(`ExpiringSoonVipUsers`) để không lệch với các tab khác.

#### A11y

- 4 ô tìm kiếm trong thanh công cụ chỉ có placeholder (placeholder-as-label) → thêm
  `aria-label` đầy đủ.
- Không còn nút icon-only thiếu `aria-label`/`title`.
- Lint toàn trang: không còn chữ bị cắt thiếu ellipsis/tooltip, không dùng sai font.

#### Kết quả đo cuối cùng

| Chế độ | Số phần tử chữ đo | Lỗi tương phản | Lỗi cỡ chữ | Chữ bị cắt | Ciphertext |
|--------|------------------:|----------------:|-----------:|------------:|-----------:|
| Light  | 678 | **0** | **0** | 0 | **0** |
| Dark   | 776 | **0** | **0** | 0 | **0** |

### 5e. Sửa lỗi "lần đầu mở trang bị lỗi" — API chưa sẵn sàng

**Triệu chứng:** mở web ngay sau khi bật hệ thống → trang hiển thị trạng thái rỗng / thông báo
lỗi ("0 Vũ trụ điện ảnh", "Không tìm thấy thể loại phù hợp"). Tải lại thì bình thường.

**Nguyên nhân (đã tái hiện được):**
1. API vừa khởi động phải chạy migration + seed 40 phim + dựng lại chỉ mục tìm kiếm → mất vài giây.
2. Trang Blazor được **prerender (SSR)**: lúc render, `MovieApiClient` gọi API → connection bị từ chối.
3. `MovieApiClient` **nuốt exception** và trả về list rỗng → SSR dựng ra HTML với empty state.
4. `AddStandardResilienceHandler` **không retry** khi connection bị từ chối: `HttpRequestException`
   không có `StatusCode` nên không rơi vào nhóm transient HTTP status (408/429/5xx) → lỗi trả về
   ngay lập tức, không có lần thử lại nào.

**Cách sửa:** mở rộng điều kiện retry của resilience handler để xử lý cả lỗi kết nối:

```csharp
options.Retry.ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
    .Handle<HttpRequestException>(exception => exception.StatusCode is null)  // connection bị từ chối
    .HandleResult(response => response.StatusCode is HttpStatusCode.RequestTimeout
        || response.StatusCode == HttpStatusCode.TooManyRequests
        || (int)response.StatusCode >= 500);
```

Kèm `MaxRetryAttempts = 6`, `Delay = 1s`, `MaxDelay = 4s` (≈ 19 giây) và
`TotalRequestTimeout = 60s`.

**Kết quả kiểm chứng (stack Docker, API bật lại 4 giây sau khi mở trang):**

| Trạng thái | Trước | Sau |
|-------------|-------|-----|
| Số thẻ hiển thị | 0 | **15** |
| Empty state | có | **không** |
| Dòng thống kê | trống | **"Hiển thị 15 / 15 thể loại"** |

Trang tự đầy dữ liệu khi API sẵn sàng, **không cần tải lại**.

> Ghi chú: hướng thay thế đã thử và bị bỏ — cho client chờ API sẵn sàng rồi mới lắng nghe cổng.
> Cách đó làm trình duyệt gặp `ERR_CONNECTION_REFUSED` nếu mở trang quá sớm, tệ hơn hẳn so với
> trang chờ rồi tự hiện nội dung.

### 5d. Tối ưu đường tìm kiếm toàn văn (PostgreSQL tsvector)

Đo trên test `PostgreSqlTsVector_ExecutionPerformance_1000Iterations_SubMillisecondPerQuery`
(ngưỡng `< 5ms` mỗi truy vấn) cho thấy truy vấn `tsvector` thực ra rất nhanh — **0,66 ms** —
nhưng `SearchMoviesAsync` mất **4,91 ms**. Phần chênh lệch nằm ở overhead EF Core, không
phải ở PostgreSQL:

| Hạng mục | Trước | Sau |
|----------|-------|-----|
| Vòng truy vấn "giao nhau" id (thừa khi không có bộ lọc thể loại/quốc gia/năm/loại) | 1 vòng | **bỏ hẳn** |
| Nạp kết quả (`Include(MovieGenres).ThenInclude(Genre)` → EF tách 3 câu lệnh) | 3 vòng | **1 vòng** (chiếu thẳng ra `MovieDto`) |
| **Tổng `SearchMoviesAsync`** | **4,91 ms** | **3,29 ms** (-33%) |

Không đổi hành vi: thứ tự kết quả vẫn theo `ts_rank_cd` (độ liên quan), `TotalItems` vẫn phản
ánh đúng tập kết quả sau khi áp bộ lọc. Test hiện **3/3 lần chạy đều PASS** (trước đó dao
động 5,0–10,7 ms và fail ngẫu nhiên vì sát ngưỡng).


### 5e. Tối ưu khu vực Admin

**Design read**: cockpit nội bộ cho vận hành viên, ngôn ngữ "control-room" dày dữ liệu, một accent đỏ
duy nhất, native CSS token. `DESIGN_VARIANCE: 4` (lưới dự đoán) · `MOTION_INTENSITY: 3` (chỉ
hover/focus) · `VISUAL_DENSITY: 8` (dữ liệu dày).

#### Đã sửa

| Nhóm | Thay đổi |
|------|----------|
| **Phân trang server-side** | Cả 5 endpoint paged (`GetMoviesPagedAsync`, `GetUsersPagedAsync`, `GetReviewsPagedAsync`, `GetPaymentsPagedAsync`, `GetAuditLogsPagedAsync`) đã được dùng. Trước đó mọi trang kéo tối đa 1000 bản ghi về client rồi lọc bằng tay → mất dữ liệu khi DB > 1000 dòng và mỗi lần filter là O(n). |
| **Bộ lọc server-side** | Server bổ sung filter còn thiếu: movies (`yearFrom`/`yearTo`/`maxRating`/`country` + map đúng tên `sortBy` mà UI gửi), users (`role`/`lockStatus`/`vipStatus` gồm `expiring_soon`/`registeredPeriod`), reviews (`status`/`minRating`/`maxRating`), payments (`method`). UI và server giờ nói cùng một ngôn ngữ. |
| **An toàn dữ liệu** | Component `AdminConfirmDialog` dùng chung (variant danger/warning, đóng bằng Esc, bắt gõ tên để xác nhận). Gắn vào: xóa phim, xóa hàng loạt, xóa đánh giá, xóa tập phim, xóa nguồn phát, khoá tài khoản, cấp/thu quyền admin, thu hồi VIP, cấp VIP trực tiếp. Trước đó tất cả gọi API thẳng — một click nhầm là mất dữ liệu không hoàn tác được. |
| **Số liệu thật thay số giả** | `AdminOverview`: bỏ biểu đồ doanh thu SVG hardcode, bỏ 4 "node CDN 100% Online" giả, bỏ fallback giả (`1h 15m`, `42.5 MB`, `28`). Thay bằng `GenreDistribution`, `TotalRevenue`, `TotalTransactions` lấy từ API. Bỏ mũi tên `▲` trên các dòng số đếm (mũi tên "tăng trưởng" trên số đếm là sai ngữ nghĩa). |
| **Số đếm nhất quán** | `AdminUsers`: trước đó tab "VIP" đếm theo `IsVip` (còn hạn) còn bộ lọc theo `Membership` (bất kể hạn) → số đếm ≠ số kết quả. Thống nhất, bổ sung `VipMembers`/`AdminUsers`/`LockedUsers` vào `AdminDashboardDto`. `AdminVip`: "Tỷ lệ thanh toán" hiện 100% khi không có giao dịch → giờ 0%; "Doanh thu thực nhận" cộng trên tập đã tải (sai) → lấy `TotalRevenue` từ server. |
| **Phân trang UI** | `AdminPagination` nâng cấp: chọn số mục/trang, "đi tới trang", nút trang đầu/cuối, `aria-current="page"`, `aria-label` cho nút ‹ ›, `aria-live` cho dòng thống kê. Thêm `AdminPagination` cho `AdminAuditLogs` (trước không có) và cho mọi trang danh sách. |
| **Bộ lọc minh bạch** | `filter-active-strip` (giống AdminMovies) cho `AdminVip` + `AdminReviews`: thấy bộ lọc đang áp dụng, xoá từng bộ lọc, "Xóa tất cả bộ lọc". Sửa "Tất cả" ở `AdminVip`/`AdminReviews` chỉ reset một phần bộ lọc khiến 2 tab cùng sáng. |
| **Trạng thái & lỗi** | `AdminEpisodes`: `isLoadingEpisodes` khởi tạo sai (hiện empty-state trước khi tải xong) → có skeleton thật; `AddStreamSource`/`DeleteStreamSource`/`ExecuteProbe` nuốt lỗi → có báo lỗi rõ ràng; drawer 360° có trạng thái tải. `AdminAuditLogs`: ô tìm kiếm báo "đang áp dụng" nhưng chưa lọc → debounce 400ms + chip "Chưa áp dụng". |
| **Lệnh chết** | 4 lệnh "Hành Động Nhanh" trong Command Palette trỏ tới `?action=new`, `?action=batch`, `?action=probe`, `?filter=pending` nhưng không trang nào đọc → nay cả 4 đã nối: `AdminMovies` mở modal thêm phim, `AdminEpisodes` mở modal tạo tập hàng loạt / modal probe, `AdminVip` mở tab giao dịch đang lọc "chờ duyệt". |
| **A11y** | Thêm focus ring `:focus-visible` cho toàn bộ control trong cockpit (trước **không có rule nào**), focus ring cho switch ẩn, `prefers-reduced-motion` tắt animation lặp, `role="dialog"`/`aria-modal`/`aria-selected` cho Command Palette, `aria-current="page"` cho phân trang, nút icon-only có `aria-label`. |
| **Light mode** | 11 nhóm badge/pill của `AdminVip` fail WCAG AA trên nền sáng (tương phản 1.2–2.9:1) → thêm override light (text đậm + viền rõ). Sửa typo class `monospace-num` → `font-mono-num` (Audit Logs mất font mono). |
| **Design token** | Thêm hệ token `--admin-bg-*`, `--admin-line-*`, `--admin-text-*`, `--admin-accent`, `--admin-status-*`, `--admin-radius-*` — trước đó admin hardcode 10+ màu nền và 8 biến thể viền. |
| **Quản lý thể loại** | Server có sẵn CRUD thể loại nhưng client không có method → bổ sung `GetGenresAsync`/`CreateGenreAsync`/`UpdateGenreAsync`/`DeleteGenreAsync` (chuyển `UpsertGenreRequest` sang Shared). |

---


#### Quản trị viên tự tải / thay ảnh nền cho từng tập

Mỗi tập đã có ảnh nền riêng (sinh động), nhưng quản trị viên chưa thể can thiệp. Nay trong
`/admin/episodes` mỗi dòng tập có ô **Ảnh nền** (ảnh thu nhỏ 96×54 + nút bút chì) bấm vào mở
modal tải ảnh lên.

| | Trước | Sau |
|---|-------|-----|
| Thao tác | Chỉ xem ảnh do hệ thống sinh | **Tải ảnh lên / thay ảnh / xoá ảnh** cho từng tập |
| Nơi lưu ảnh | — | `/app/data/episodes/ep-{episodeId}.{ext}` — volume `server_data` nên **không mất khi build lại container** |
| CSDL | `ThumbnailUrl` trỏ ảnh sinh | `ThumbnailUrl` trỏ `/api/episodes/{episodeId}/media?v={mili-giây}` |
| API | — | `POST /api/admin/episodes/{id}/thumbnail` (multipart) · `DELETE /api/admin/episodes/{id}/thumbnail` · `GET /api/episodes/{id}/media` (công khai) |

**Bốn chi tiết kỹ thuật đã phải xử lý:**

1. **URL ảnh phải là URL tuyệt đối.** Thẻ `<img>` của trình duyệt phân giải `/api/...` theo
   origin **của trang web** (`:5229`), không phải của API (`:5085`) → ảnh không tải được.
   Server phải đổi thành URL tuyệt đối bằng `PublicUrlResolver` (cùng cơ chế với URL phát video).
   Áp dụng cho cả 3 API: `EpisodesController`, `MoviesController` (trang xem phim) và
   `AdminEpisodesController` (khối quản trị).
2. **Kiểm tra chữ ký nhị phân, không tin `Content-Type` của trình duyệt.** File tải lên phải
   khớp magic number của JPG/PNG/WEBP/GIF/AVIF — nếu chỉ tin đuôi file thì một tệp đổi tên
   (`.png` nhưng thật ra là script) vẫn lọt vào.
3. **Giới hạn kích thước theo từng endpoint.** Toàn bộ API bị chặn ở 64 KB bằng filter
   `RequestSizeLimit`; riêng endpoint tải ảnh dùng `[RequestSizeLimit(5 MB)]` nên Kestrel phải
   nới mức tối đa lên 6 MB — filter toàn cục vẫn giữ nguyên 64 KB cho các endpoint khác.
   Bên client, Blazor Server chuyển tệp qua SignalR (mặc định 32 KB/tin nhắn) nên phải nới
   `MaximumReceiveMessageSize` lên 4 MB, còn tệp tải lên bị giới hạn 2 MB ở tầng UI.
4. **`RepairEpisodeThumbnailsAsync` phải bỏ qua ảnh tự tải lên.** Nếu không, mỗi lần khởi
   động server seeder sẽ ghi đè URL `/media` về ảnh sinh và xoá luôn ảnh do quản trị viên đặt.

**Kiểm chứng trên stack thật** (`docker compose`): tải 3 tập → file nằm đúng trong volume
`/app/data/episodes` → URL công khai trả `200 image/png` đúng số byte → tệp giả bị từ chối →
xoá ảnh thì tập quay về ảnh mặc định, các tập khác giữ nguyên ảnh riêng. Tải ảnh **qua giao diện**
(trình duyệt chọn tệp → xem trước → Lưu) cũng chạy đúng, ảnh mới hiện ngay trong bảng và
trên trang `/watch/{id}`.

**Tương phản (đo trực tiếp trên trình duyệt):** dark mode 12,8–16,1:1 · light mode 3,85–10,8:1
(icon nút sửa ảnh dùng `--text-2` vì `--muted` chỉ đạt 2,1:1 trên nền sáng).


#### Ảnh nền thật cho từng tập (343 ảnh dựng sẵn)

Sau khi mở tính năng tải ảnh cho quản trị viên, ảnh nền mặc định của các tập vẫn là **gradient
sinh động** — nhìn "ảnh" nhưng không phải ảnh thật. Nay mỗi tập trong danh mục có **một ảnh
riêng** dựng sẵn trong `wwwroot/media/episodes/`.

| | Trước | Sau |
|---|-------|-----|
| Nguồn ảnh | SVG sinh động theo màu | **Poster/backdrop thật của chính phim đó**, cắt riêng cho từng tập |
| Đường dẫn | `/api/episodes/{m}/{n}/thumbnail` | `/media/episodes/ep-{movieId}-{n:00}.webp` (WebP 640×360) |
| Số lượng | — | **343 ảnh** (40 phim · 343 tập) · ~23 KB/ảnh · tổng 7,8 MB |

**Vì sao nằm ở `wwwroot/media/episodes` chứ không ở `wwwroot/images`:** kho `wwwroot/images` là
artwork dùng chung cho toàn site và đang bị hai test kích thước giữ dưới **10 MB**
(`ImagesDirectory_StaysUnderTenMegabytes`, `TotalImagesDirectorySize_IsReducedUnder15MB`).
343 ảnh tập (7,8 MB) không thể nằm chung mà vẫn giữ được ngưỡng đó, nên ảnh theo tập được tách
riêng sang `wwwroot/media/episodes` — kho `images` giữ nguyên 8,17 MB. Kho media mới cũng được
test riêng (`EpisodeThumbnailMediaDirectory_StaysUnderNineMegabytes`) và có test bảo đảm **mỗi tập
trong CSDL đều có đúng một tệp ảnh và không tập nào trùng ảnh**
(`EverySeededEpisode_HasItsOwnThumbnailFile`).

**Cách dựng ảnh** (mỗi tập một ảnh khác nhau, không trùng):

1. Nguồn: ảnh `movie-{id}-backdrop.webp` (ngang) và `movie-{id}-poster.webp` (dọc) của phim.
2. **Cắt cửa sổ khác nhau theo số tập** — `x = (n × 37) % maxX`, `y = (n × 53) % maxY`, phóng
   `1.0 + (n % 5) × 0.05` → hai tập liền nhau không bao giờ trùng khung hình.
3. **Tint màu theo tập** — `hue = (movieId × 47 + n × 23) % 360`, độ đậm 10–18% → mỗi tập một
   sắc thái riêng, đồng bộ với bảng màu của ảnh SVG sinh sẵn.
4. **Gradient tối ở đáy + số tập** ("TẬP 07" và số lớn mờ góc phải) → nhìn như ảnh cảnh phim.

**Vì sao dùng canvas của trình duyệt thay vì C#?** `System.Drawing` không giải mã được WebP
(`Out of memory`) và chỉ chạy được trên Windows, còn container server chạy Linux. Dùng
`<canvas>` của trình duyệt vừa giải mã được WebP, vừa xuất được **WebP** (nhẹ hơn JPEG ~40%),
vừa đặt được overlay chữ. Ảnh được gửi về máy qua một HTTP receiver tạm (`HttpListener`).

**Nguồn ảnh ngoài đã thử:** Wikipedia API (REST summary + `generator=images`) và Wikimedia
Commons — tải được **155 ảnh thật** của các phim/series. Nhưng ảnh Commons của phim chủ yếu là
**ảnh sự kiện** (diễn viên tại hội chớp) hoặc **logo** — không hợp làm ảnh cảnh tập, nên ảnh nền
vẫn lấy từ poster/backdrop chính thức của phim trong repo (đã tải về từ Internet ở giai đoạn dựng
danh mục, đúng chủ đề và đúng phim, đồng bộ style toàn site).

**Tương thích ngược:** ảnh sinh động của API vẫn còn, làm **dự phòng** cho tập mà quản trị
viên tạo mới (chưa có file tĩnh) — `AdminEpisodesController.AddEpisode` và `BatchGenerateEpisodes`
trỏ tập mới về `/api/episodes/{m}/{n}/thumbnail`. `RepairEpisodeThumbnailsAsync` chỉ sửa những tập
đang trỏ ảnh bìa chung hoặc để trống, **không** đụng tới ảnh tĩnh, ảnh sinh động hay ảnh quản
trị viên tải lên.

**Kiểm chứng:** `/watch/33` → 28 ảnh · 28 URL khác nhau · **28/28 tải thành công** (640×360) ·
mỗi tập một khung hình khác nhau ✓

## 6. Ghi chú quyết định thiết kế

1. **Giữ MySQL** làm provider phụ — đã có code + script `database/mysql`, gỡ bỏ nằm ngoài
   phạm vi yêu cầu. PostgreSQL là provider **chính**.
2. **Bỏ SQLite khỏi runtime**, nhưng **không** dùng SQLite làm "đôi" test nữa — test
   relational chạy trên PostgreSQL thật để kiểm chứng đúng hành vi sẽ chạy production.
3. **Không** viết lại `AppDbContext` mapping kiểu: EF Core + Npgsql đã map đúng; chỉ cần
   thêm converter `DateTime`.
4. **Trigger** thay vì generated column vì `cast_members` là bảng con — đây là khác biệt
   duy nhất so với SQLite và được xử lý bằng 1 trigger bổ sung.
