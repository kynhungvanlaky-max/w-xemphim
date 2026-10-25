# Kế hoạch chi tiết: Quản lý nguồn Live, bảo mật phát video và tối ưu đường truyền

> Phạm vi: CinemaFlow (`WebXemPhim.slnx`) — ASP.NET Core Web API, Blazor Web App, PostgreSQL, player HLS/MP4.
> Mục tiêu: vận hành nguồn phát video (VOD + Live) an toàn, chống lạm dụng, giảm chi phí băng thông, tăng chất lượng xem và có khả năng quan sát (observability).

---

## 0. Tóm tắt điều hành

Hệ thống hiện tại đã có nền tảng tốt: ký URL HMAC, gắn IP, giới hạn thời gian, lease phiên xem qua SignalR, watermark canvas, rate limit toàn cục và proxy Range cho video. Tuy nhiên, khi đi vào vận hành thật với nguồn live/HLS, có **5 lỗ hổng/điểm nghẽn nghiêm trọng**:

1. **HLS chưa được proxy/rewrite đúng**: manifest `.m3u8` được trả nguyên bản; segment có thể trỏ thẳng về origin (lộ CDN, bỏ qua chữ ký) hoặc trỏ sai đường dẫn `/api/stream/...` (404).
2. **Signed URL sống quá lâu và không gắn phiên**: TTL cấu hình 120 phút, không có `sid/jti/kid`; URL bị lộ trong 2 giờ vẫn dùng được từ cùng IP, không thu hồi được.
3. **`/api/stream/play` không kiểm tra lease/session**: chỉ kiểm tra chữ ký; người đã bị evict hoặc logout vẫn phát được nếu còn URL.
4. **Lưu URL stream không kiểm tra SSRF**: chỉ nút Probe mới validate; admin có thể lưu `http://169.254.169.254/...` hoặc host nội bộ rồi server proxy tới.
5. **Rate limit toàn cục 300/phút áp lên cả segment HLS**: một người xem có thể tạo 30–60 request/phút; nhiều người sau NAT dùng chung IP sẽ bị chặn oan, trong khi kẻ lạm dụng vẫn có thể rải request.

Kế hoạch chia làm 3 track song song:

- **Track A — Bảo mật phát video (P0)**: token ngắn hạn gắn phiên, lease/concurrency theo hạng thành viên, SSRF guard, thu hồi tức thời, chống chia sẻ.
- **Track B — Quản lý nguồn Live (P1)**: mô hình dữ liệu Live Channel/Source/EPG, admin workspace, health monitoring, failover, player LL-HLS.
- **Track C — Tối ưu đường truyền (P0/P1)**: đưa CDN/edge ra trước video, cache segment, HTTP/2-3, ABR/LL-HLS, giảm egress qua app, telemetry QoE.

**Hướng đã chốt**: làm Track A + C trước (1–2 tuần) để chặn rò rỉ và ổn định VOD; sau đó làm Track B theo hướng **tự host MediaMTX + ffmpeg + Shaka Packager** (phương án B). Toàn bộ transcoder/packager chạy như dịch vụ media riêng, **không** nhúng vào ASP.NET Core; API .NET chỉ giữ vai trò control plane (metadata, token, lease, health, EPG). Giai đoạn đầu dùng **Nginx/Caddy edge** trước app để cache segment và giảm egress; khi có ngân sách thì thay bằng CDN thật mà không đổi API client.

---

## 1. Hiện trạng và khoảng trống

### 1.1. Luồng phát hiện tại

```
Admin (/admin/episodes)
  └─ CRUD StreamSources (EpisodeId + Position, ServerName, Quality, StreamUrl)

Client (/watch/{id})
  └─ GET /api/movies/{id}
       └─ MoviesController.MaskVipStreams()
            ├─ che URL nguồn VIP nếu không phải VIP
            └─ ký mọi URL còn lại: /api/stream/play?episodeId&pos&exp&ip&sig
  └─ <video> hoặc hls.js
       └─ GET /api/stream/play
            └─ proxy Range tới origin, trả bytes
  └─ SignalR lease + heartbeat 30s (giới hạn 1 phiên)
```

### 1.2. Bảng phát hiện

| # | Mức | Vấn đề | Bằng chứng | Tác động |
|---|-----|--------|------------|----------|
| F1 | **P0** | HLS manifest không được rewrite/proxy đúng | `StreamController.PlayStream` copy nguyên bytes (`StreamController.cs:110-160`) | Segment tương đối → 404; segment tuyệt đối → trình duyệt gọi thẳng origin, lộ CDN và bỏ qua chữ ký |
| F2 | **P0** | Signed URL TTL quá dài, không gắn phiên | `appsettings.json:13` (`StreamTtlMinutes: 120`), `StreamUrlSigner.cs:99-105` chỉ có `ep/pos/exp/ip` | URL rò rỉ dùng lại được 2 giờ; không revoke được khi logout/evict/ban |
| F3 | **P0** | `/api/stream/play` không kiểm tra lease | `StreamController.cs:66-163` không inject `IStreamSessionService` | Người bị evict vẫn phát nếu còn URL; giới hạn thiết bị bị vô hiệu |
| F4 | **P0** | Lưu URL stream không chống SSRF | `AdminEpisodesController.cs:416-477` không gọi `SafeStreamUrlValidator`; chỉ probe gọi (`:492-547`) | Admin/bị chiếm tài khoản có thể trỏ server proxy vào metadata cloud, DB nội bộ, dịch vụ nội bộ |
| F5 | **P1** | Rate limit global áp lên segment | `ApiRateLimits.cs:97-99` — 300 request/phút theo user/IP | HLS nhiều segment + nhiều người sau NAT bị 429; không có policy riêng cho playback |
| F6 | **P1** | Giới hạn đồng thời hardcode = 1 | `StreamSessionService.cs:429-437`; `ActiveStreamsCount = 1` (`:103,210`) | Không phân biệt Free/VIP; VIP trả tiền vẫn bị đá khi xem 2 thiết bị; số liệu trả về sai |
| F7 | **P1** | Không có health check/failover tự động | Chỉ probe thủ công; ping client hardcode (`WatchPlaybackState.cs:84-87`) | Nguồn chết vẫn được chọn; người xem phải bấm "Dùng nguồn khác" |
| F8 | **P1** | Không có telemetry QoE | Không có endpoint/metric cho startup, rebuffer, bitrate, lỗi | Không biết nguồn nào lỗi, không đo được cải tiến |
| F9 | **P1** | Proxy là điểm nghẽn đơn, không CDN/cache | `Program.cs:63-72`, `StreamController.cs:152-155` (`no-store`) | Toàn bộ băng thông video đi qua app; chi phí egress lớn; khó scale ngang |
| F10 | **P1** | Không có mô hình Live | `StreamSourceEntity` chỉ có `EpisodeId/Position/ServerName/ServerType/Quality/StreamUrl` (`CatalogEntities.cs:73-82`) | Không quản lý được kênh live, EPG, DVR, độ trễ, priority, vùng |
| F11 | **P2** | Watermark chỉ ở client | Canvas trong `CinemaPlayer.razor:26-36`, `cinema-player.js:555-617` | Người xem xoá canvas/che watermark dễ dàng; không truy vết được bản ghi lậu |
| F12 | **P2** | Không DRM/AES | Chỉ signed URL, stream plain | Ai lấy được URL/segment có thể tải và phát lại ngoài hệ thống |
| F13 | **P2** | HLS config thiếu LL/ABR tuning | `cinema-player.js:637-642` chỉ có worker/backBuffer/capLevel | Live latency cao, ABR khởi động chậm, dễ rebuffer trên mạng yếu |
| F14 | **P2** | Chưa có key rotation | Một key tĩnh; không có `kid` | Rotate key phải redeploy và làm hỏng mọi URL đang phát |
| F15 | **P2** | Multi-instance chưa sẵn sàng | `_userLocks` in-memory (`StreamSessionService.cs:21`); cache VIP in-memory | Chạy 2 instance có thể race lease/cache; cần Redis hoặc DB row lock |

### 1.3. Điểm mạnh có thể tái sử dụng

- `StreamUrlSigner` đã dùng HMAC-SHA256 + `FixedTimeEquals`, có validate production key.
- `SafeStreamUrlValidator` đã chặn private IP, metadata IP, port nguy hiểm, DNS resolution.
- `StreamSessionService` đã có lease, heartbeat, evict, prune, force disconnect qua SignalR.
- `CinemaPlayer` đã có HLS level switching, error recovery, watermark, PiP, keyboard.
- `WebSecurityExtensions` đã có security headers, CORS allowlist, rate limit, HTTPS/HSTS production.
- Test suite lớn (1.225 test) là lưới an toàn tốt để refactor.

---

## 2. Nguyên tắc thiết kế

1. **Zero-trust playback**: mỗi lần phát cần một vé ngắn hạn gắn người dùng + phiên + nội dung + thiết bị; không tin URL cũ.
2. **Control plane tách data plane**: API chỉ cấp token/lease/metadata; video bytes đi qua CDN/edge, không đi qua app ở quy mô lớn.
3. **Deny by default**: URL stream phải qua allowlist scheme/host/port; fail closed khi nghi ngờ.
4. **Short-lived & revocable**: token 5–15 phút, refresh được, revoke tức thời; segment immutable nhưng token ngắn.
5. **Đo trước khi tối ưu**: mọi thay đổi phải có metric trước/sau (startup, rebuffer, egress, lỗi).
6. **Failover có kiểm soát**: tự chuyển nguồn nhưng có cooldown, tránh flapping; ưu tiên nguồn khỏe gần người xem.
7. **Không log bí mật**: không log `sig`, `token`, `ip` đầy đủ, URL gốc của CDN.
8. **Tương thích ngược**: VOD hiện tại phải tiếp tục chạy trong suốt quá trình migration.

---

## 3. Kiến trúc đích

### 3.1. Sơ đồ tổng thể

```
                         ┌─────────────────────────────┐
                         │        Admin Workspace       │
                         │  /admin/live  /admin/episodes│
                         └──────────────┬──────────────┘
                                        │ REST (Admin JWT + MFA)
                         ┌──────────────▼──────────────┐
                         │      Playback Control API    │
                         │  session / token / lease     │
                         │  source registry / EPG       │
                         └───────┬──────────────┬──────┘
                                 │              │
                    ┌────────────▼───┐   ┌──────▼─────────────┐
                    │ Health Monitor │   │ PostgreSQL          │
                    │ Worker + Score │   │ channels/sources/   │
                    └────────┬───────┘   │ sessions/health/EPG │
                             │           └─────────────────────┘
                             │
        ┌────────────────────▼─────────────────────┐
        │              Delivery Plane               │
        │  CDN / Edge (Nginx, Caddy, CloudFront...) │
        │  - signed cookie / token validation       │
        │  - segment cache, HTTP/2-3, TLS 1.3       │
        └────────────────────┬─────────────────────┘
                             │
              ┌──────────────▼──────────────┐
              │        Origin / Packager     │
              │  LL-HLS / HLS / DASH         │
              │  MediaMTX / ffmpeg / Shaka   │
              └──────────────┬──────────────┘
                             │
                  ┌──────────▼──────────┐
                  │  Encoder / Provider  │
                  │  RTMP / SRT / HLS    │
                  └─────────────────────┘

Client (/watch, /live) ──► Control API ──► token + manifest URL
       │                                      │
       └──────────► CDN/Edge ─────────────────┘
                    (manifest + segments)
```

### 3.2. Ba phương án quản lý Live

| Phương án | Mô tả | Ưu | Nhược | Công sức | Trạng thái |
|-----------|-------|----|-------|----------|------------|
| **A. External HLS** | Kênh live do provider/encoder bên ngoài đẩy ra HLS; CinemaFlow chỉ quản lý metadata, ký URL, health, EPG | Nhanh, ít ops, không cần transcode | Phụ thuộc provider; khó kiểm soát latency | 5–8 ngày | Phương án dự phòng |
| **B. Self-host ingest** | MediaMTX nhận RTMP/SRT, ffmpeg transcode, Shaka Packager ra LL-HLS, origin + Nginx/Caddy edge | Tự chủ, kiểm soát latency/DRM | Cần GPU/CPU, vận hành phức tạp, tốn người | 15–25 ngày + ops | **ĐÃ CHỌN** |
| **C. Managed live** | Mux / Cloudflare Stream / Bitmovin / AWS MediaLive | Ít ops, scale tốt, có DRM/analytics | Chi phí theo phút, lock-in | 5–10 ngày | Phương án dự phòng |

**Quyết định đã chốt (2026-10-25)**: triển khai **phương án B — self-host MediaMTX**. Interface client/API giữ trừu tượng (`protocol`, `playbackUrl`, `region`, `priority`) để có thể chuyển sang A/C sau mà không đổi contract. Track B vì vậy tăng thêm phần hạ tầng media (mục 3.3) và được đẩy sang Phase 2 sau khi Track A + C ổn định.

### 3.3. Kiến trúc MediaMTX đã chọn

#### 3.3.1. Sơ đồ thành phần

```
Encoder / Camera / OBS
   │ RTMP 1935 (publish auth) hoặc SRT 8890 (passphrase)
   ▼
┌──────────────────────────────────────────────┐
│ MediaMTX (container, network host hoặc macvlan)│
│  - path: live/{channelSlug}                  │
│  - runOnPublish: ffmpeg transcode ladder     │
│  - runOnReady: health hook → API             │
│  - HLS muxer (LL-HLS) → /hls/{slug}/index.m3u8│
│  - recording (tùy chọn) → /recordings        │
└───────────────┬──────────────────────────────┘
                │ filesystem / internal HTTP
                ▼
┌──────────────────────────────────────────────┐
│ Nginx/Caddy edge (origin shield nội bộ)      │
│  - cache segment immutable, manifest TTL 2–6s│
│  - auth_request → Playback Control API       │
│  - HTTP/2, TLS 1.3, Brotli manifest          │
└───────────────┬──────────────────────────────┘
                │ HTTPS
                ▼
        Client /live/{slug} (hls.js LL-HLS)
                │
                └── Control API: session + token + EPG + telemetry
```

#### 3.3.2. Transcode ladder & packaging

| Rendition | Resolution | Bitrate video | Audio | Ghi chú |
|-----------|------------|---------------|-------|---------|
| 240p | 426×240 | 400 kbps | AAC 64k | Mạng yếu / mobile |
| 360p | 640×360 | 800 kbps | AAC 96k | |
| 480p | 854×480 | 1.4 Mbps | AAC 128k | |
| 720p | 1280×720 | 2.8 Mbps | AAC 128k | Mặc định mobile |
| 1080p | 1920×1080 | 5 Mbps | AAC 128k | Mặc định desktop |
| 1440p/4K | tùy nguồn | 12–25 Mbps | AAC 192k | Chỉ khi nguồn đủ và có GPU |

- **Live chuẩn**: segment 2s, `#EXT-X-TARGETDURATION:4`, latency 8–15s.
- **LL-HLS**: part 0.5s, hold-back 1.5–3s, latency 3–5s; chỉ bật cho kênh cần tương tác (thể thao, sự kiện).
- **Packager**: ưu tiên HLS muxer sẵn có của MediaMTX cho MVP; khi cần multi-rendition + DRM chuyển sang **Shaka Packager** đọc fMP4 từ ffmpeg.
- **Audio**: AAC-LC 128k; loudness normalize `-17 LUFS`; subtitle WebVTT nếu có.
- **Keyframe**: `-g 50 -keyint_min 50 -sc_threshold 0` (2s @ 25fps) để segment cắt đúng.

#### 3.3.3. Bảo mật ingest

1. **Publish auth**: MediaMTX `authMethod: http` → gọi `POST /api/internal/live/authorize` với `path`, `action=publish`, `user`, `password`/token; API kiểm tra channel + stream key hash + IP allowlist, trả 200/401. Không dùng chung một key cho mọi kênh.
2. **SRT**: bật `srtPassphrase` riêng từng kênh; ưu tiên SRT cho nguồn qua Internet (chống mất gói tốt hơn RTMP).
3. **RTMPS**: nếu encoder hỗ trợ, terminate TLS ở Nginx trước MediaMTX; không mở RTMP thô ra Internet.
4. **Firewall**: chỉ mở 1935/8890 cho IP encoder; HLS origin chỉ cho edge truy cập; API internal chỉ nghe loopback/private network.
5. **Stream key**: sinh 32 byte, lưu hash (PBKDF2/SHA-256 + pepper) trong `live_sources`, cho phép rotate; không hiển thị lại sau khi tạo.
6. **Hook**: `runOnReady`/`runOnNotReady` gọi API để cập nhật `IsLive`, `LastStartedAtUtc`; kèm shared secret HMAC + timestamp chống replay.

#### 3.3.4. Triển khai & vận hành

- **Docker Compose** service `mediamtx` + volume `hls/`, `recordings/`, `mediamtx.yml`; healthcheck `GET /v3/paths/list`.
- **Tài nguyên**: 1 vCPU + 2 GB cho 5–10 kênh 720p software transcode; GPU (NVENC/QSV) nếu nhiều kênh 1080p/4K. Đo bằng `docker stats` + metric ffmpeg.
- **Scale**: mỗi instance MediaMTX phục vụ một nhóm kênh; API trả `originBaseUrl` theo region; edge/CDN là lớp scale ngang.
- **Recording/DVR**: ghi fMP4 segment 2s, giữ `DvrWindowMinutes`; dọn file cũ bằng job; không ghi vĩnh viễn mặc định.
- **Backup/DR**: cấu hình + stream key lưu trong secret manager; nếu MediaMTX chết, API đánh dấu nguồn Down và chuyển nguồn dự phòng (A/C hoặc encoder thứ hai).
- **Không chạy MediaMTX trong cùng container/app ASP.NET**; tách lifecycle để nâng cấp độc lập.


### 3.4. Thành phần và trách nhiệm

| Thành phần | Trách nhiệm | Công nghệ đề xuất |
|------------|-------------|-------------------|
| Admin Workspace | CRUD kênh/nguồn, EPG, health, probe, audit | Blazor + REST |
| Playback Control API | Session, token, lease, source selection, failover | ASP.NET Core |
| Source Registry | Lưu kênh, nguồn, priority, region, health | PostgreSQL |
| Health Monitor | Probe định kỳ, chấm điểm, phát hiện down | `BackgroundService` + `HttpClient` |
| Delivery Plane | Cache, auth token, TLS, HTTP/3, range | CDN + Nginx/Caddy |
| Origin/Packager | Đóng gói HLS/LL-HLS/DASH, DRM | MediaMTX/ffmpeg/Shaka |
| Client Player | ABR, LL-HLS, failover, telemetry | hls.js/Shaka + Blazor |
| Observability | Metrics, traces, logs, alert | OpenTelemetry + Prometheus/Grafana/Loki |

---

## 4. Track A — Bảo mật phát video (P0)

### A1. Mô hình token & phiên phát

**Mục tiêu**: thay "URL ký dài hạn" bằng "vé ngắn hạn gắn phiên", có thể thu hồi.

#### A1.1. Luồng cấp vé

```
1. Client đã đăng nhập, gọi POST /api/stream/sessions
   body: { episodeId | channelId, position?, deviceId, sessionId? }
2. Server kiểm tra:
   - authenticated + VIP (nếu nguồn VIP/4K)
   - lease/concurrency (IStreamSessionService)
   - nguồn enabled + healthy + URL safe
3. Server tạo playback_session (DB) + token ngắn hạn
4. Trả về: { sessionId, token, manifestUrl, expiresAt, refreshAfter, lease }
5. Client tải manifest kèm token; edge/app set cookie HttpOnly cho segment
6. Heartbeat 30s; refresh token ở 70% TTL
7. Logout/evict/ban → revoke session; token còn hạn cũng bị từ chối
```

#### A1.2. Token format

Khuyến nghị token HMAC tự chứa (không cần JWT library):

```
v1.<base64url(payload_json)>.<base64url(hmac_sha256(payload, key))>
```

Payload tối thiểu:

| Claim | Ý nghĩa | Ví dụ |
|-------|---------|-------|
| `v` | Version | `1` |
| `kid` | Key id để rotate | `2026-10` |
| `sid` | Playback session id | `8f3c...` |
| `uid` | User id (hash ngắn) | `u_...` |
| `cid` | Content id (`ep:123` / `ch:45`) | `ep:123` |
| `pos` | Stream position | `2` |
| `scope` | `play` / `manifest` / `segment` | `play` |
| `tier` | Hạng thành viên | `Vip` |
| `ip` | HMAC(IP) — không lưu IP thô | `a1b2...` |
| `dev` | Hash deviceId (tuỳ chọn) | `d_...` |
| `iat` | Issued at (unix) | `1760000000` |
| `nbf` | Not before (unix) | `1760000000` |
| `exp` | Expiry (unix) | `1760000600` |
| `jti` | ID duy nhất, chống replay log | `...` |

**TTL đề xuất (đã chốt)**:

| Loại | TTL | Ghi chú |
|------|-----|---------|
| Manifest/token VOD | 10 phút | Refresh ở 70% TTL (7 phút) |
| Manifest/token Live | 5 phút | Refresh ở 70% TTL (3.5 phút); giảm cửa sổ lộ URL |
| Segment cookie | 10 phút (VOD) / 5 phút (Live) | Trượt theo manifest |
| Refresh window | 2 phút | Chỉ refresh khi lease còn sống |
| Clock skew | 30 giây | Cho phép lệch giờ |

#### A1.3. Cách gắn token cho HLS

Có 2 cách; chọn theo hạ tầng:

**Cách 1 — Same-origin proxy (đơn giản, phù hợp hiện tại)**

- `/api/stream/session/{sid}/manifest` trả playlist và set cookie:

```
Set-Cookie: cf_play=<token>; HttpOnly; Secure; SameSite=Lax; Path=/api/stream; Max-Age=600
```

- Segment request tự gửi cookie; server/edge validate cookie.
- Rewrite mọi URI trong playlist thành đường dẫn tương đối hoặc tuyệt đối trỏ về `/api/stream/session/{sid}/segment?seq=...`.
- Không đưa token vào query string để tránh lộ qua log/referrer.

**Cách 2 — CDN edge (khuyến nghị khi scale)**

- CDN ký cookie (CloudFront signed cookie) hoặc edge function validate token.
- Playlist trả về URI trỏ CDN; token ngắn hạn; edge cache segment theo path (không theo token).
- App không proxy bytes.

#### A1.4. Validation order (fail closed)

1. Parse token, kiểm tra format.
2. Tìm key theo `kid`; verify HMAC constant-time.
3. `nbf <= now + skew`, `exp >= now - skew`.
4. `scope` phù hợp endpoint.
5. `sid` tồn tại, `IsActive = true`, chưa hết hạn.
6. Lease còn hiệu lực; user không bị ban/evict.
7. IP/device policy: khớp hash; nếu đổi IP trong cùng ASN thì cho phép (cấu hình), khác ASN thì yêu cầu re-auth.
8. Nguồn `IsEnabled`, health != Down; nếu Down trả danh sách nguồn thay thế.
9. Rate limit theo session/IP.
10. Cho phép; ghi telemetry (không ghi token).

#### A1.5. Refresh & revoke

- `POST /api/stream/sessions/{sid}/refresh` → token mới nếu lease + heartbeat hợp lệ.
- `DELETE /api/stream/sessions/{sid}` → revoke ngay.
- Logout, đổi mật khẩu, admin ban, evict → gọi `RevokeUserStreamsAsync` (đã có) + đánh dấu session revoked.
- Edge cache manifest TTL 2–6 giây; segment immutable nhưng token ngắn nên không thể phát lâu sau khi hết hạn.

### A2. Lease & giới hạn đồng thời theo hạng

**Hiện tại**: `GetMaxConcurrentStreamsInternalAsync` trả default 1; `ActiveStreamsCount` luôn 1.

**Đề xuất**:

| Hạng | Max concurrent | Max device | Ghi chú |
|------|----------------|------------|---------|
| Free | 1 | 1 | Có thể xem demo/nguồn thường |
| Silver | 2 | 2 | |
| Gold | 3 | 3 | |
| Platinum | 4 | 4 | |
| Diamond | 4 | 5 | |
| Admin | 5 | 5 | Test |

- Đọc từ `StreamSecurity:MaxConcurrentStreams` (config) + override theo `MembershipTier`.
- Đếm `Distinct SessionId` đang active, không đếm connection.
- Trả `ActiveStreamsCount` đúng, `MaxAllowedStreams`, `EvictedSessionId`.
- Khóa theo user: hiện `_userLocks` in-memory; multi-instance dùng PostgreSQL advisory lock hoặc Redis `SETNX` + TTL.
- Device fingerprint: `deviceId` sinh một lần, lưu localStorage; gửi kèm session; server lưu hash; giới hạn số device.
- Heartbeat 30s, TTL 60s, grace 10s; nếu mất heartbeat 2 lần → evict.

### A3. Chống SSRF (bắt buộc)

**Bề mặt tấn công**:

- `UpsertStreamSourceRequest.StreamUrl` (admin nhập).
- `ProbeStreamRequest.StreamUrl` (admin probe).
- URL do provider trả về trong manifest (segment URI) nếu proxy/rewrite.
- Redirect từ origin.

**Quy tắc**:

1. Chỉ `https` ở production; `http` chỉ Development và phải cấu hình rõ.
2. Không userinfo (`user:pass@`), không fragment, không control chars, max 600 ký tự.
3. Resolve DNS một lần; **pin IP đã resolve** khi kết nối (chống DNS rebinding).
4. Chặn private/loopback/link-local/metadata/CGNAT/multicast/reserved (đã có trong `SafeStreamUrlValidator`).
5. Chặn port nhạy cảm (22, 23, 25, 135, 139, 445, 1433, 1521, 3306, 5432, 6379, 9200, 11211, 27017, 2375, 2376).
6. Redirect: tối đa 3 hop; validate lại từng hop; không cho redirect sang host nội bộ.
7. Timeout: connect 5s, header 10s, body 30s; giới hạn header size.
8. Egress proxy/allowlist CDN nếu có; log host bị chặn (không log full URL có token).
9. Lưu `ValidatedHost`, `ResolvedIp`, `ValidatedAtUtc`; revalidate trước khi phát nếu quá 1 giờ.
10. Với manifest HLS: validate từng segment URI trước khi rewrite/proxy; segment trỏ host lạ → từ chối.

**Áp dụng**:

- Gọi `SafeStreamUrlValidator.ValidateUrlAsync` trong `AddStreamSource` và `UpdateStreamSource` (hiện chỉ probe).
- Trả `400` với thông báo an toàn, không echo toàn bộ URL.
- Thêm test matrix IPv4/IPv6/DNS rebinding/redirect.

### A4. Bảo vệ admin & audit

- `[Authorize(Policy = "Admin")]` trên toàn bộ `AdminEpisodesController` (kiểm tra `AdminBaseController`; nếu chưa có thì bổ sung).
- **Step-up MFA** cho thao tác nhạy cảm: thêm/sửa/xoá nguồn, đổi primary, disable nguồn cuối.
- Audit trước/sau (diff), không ghi URL đầy đủ; ghi `host`, `position`, `serverName`, `quality`, `action`, `actor`, `ip hash`.
- Optimistic concurrency (`RowVersion`) cho source để tránh ghi đè khi 2 admin sửa.
- Soft delete + khôi phục 30 ngày; không xoá cứng nguồn đang phát.
- Rate limit riêng cho admin stream endpoints (ví dụ 60/phút/user).
- Cảnh báo khi admin lưu URL trỏ private IP hoặc HTTP.

### A5. Chống chia sẻ, watermark, DRM

**P1 — chống chia sẻ cơ bản**:

- Watermark hiện tại (canvas) giữ nguyên, nhưng thêm `sessionId` ngắn + timestamp + user hash; vị trí đổi ngẫu nhiên.
- Phát hiện bất thường: cùng tài khoản xuất hiện từ 2 ASN khác nhau trong 10 phút → yêu cầu xác thực lại hoặc chặn phiên cũ.
- Giới hạn device; cho phép admin xem/revoke thiết bị.
- Không cho phép `Referer` rỗng? Không đáng tin; bỏ qua.

**P2 — forensic watermark**:

- Server-side overlay (ffmpeg) hoặc packager watermark; mỗi phiên một mã khác nhau.
- Lưu mapping `sessionId → userId → timestamp` để truy vết khi phát hiện bản lậu.
- Không thể chặn quay màn hình tuyệt đối; mục tiêu là deterrence + truy vết.

**P2 — DRM/AES**:

- Ưu tiên DRM (Widevine/FairPlay/PlayReady) qua packager + license proxy nếu nội dung có bản quyền.
- Nếu chưa có DRM: HLS AES-128 với key URI được ký, rotate key mỗi 30–60s; lưu ý AES-128 không chống được người xem quyết tâm.
- Không tự viết license server; dùng giải pháp có sẵn (Shaka Packager + license service, Mux, Bitmovin).

### A6. Secrets, headers, logging

- **Key ring**: `Security:StreamSigningKeys` dạng mảng `{ Kid, Secret }`, `ActiveKid`; cho phép rotate không downtime. Key cũ chỉ để validate URL còn hạn.
- Production bắt buộc key >= 32 ký tự, không dùng default; load từ env/secret manager.
- TTL mặc định VOD 10 phút, Live 5 phút (thay 120 phút).
- Headers cho playback:
  - `Cache-Control: private, no-store` cho manifest/token.
  - `Referrer-Policy: no-referrer`.
  - `X-Content-Type-Options: nosniff`.
  - CSP `media-src 'self' blob: https://cdn...`.
  - `Permissions-Policy` hạn chế.
- Logging: cấu trúc, có `sessionId`, `contentId`, `sourceId`, `latency`, `status`; **không** log `sig`, `token`, `Cookie`, IP đầy đủ.
- CORS: chỉ origin allowlist; CDN cấu hình CORS cho manifest/segment nếu cross-origin.

---

## 5. Track B — Quản lý nguồn Live (P1)

### B1. Mô hình dữ liệu

Giữ `StreamSourceEntity` cho VOD để tương thích; thêm bảng riêng cho Live.

#### `live_channels`

| Cột | Kiểu | Ghi chú |
|-----|------|---------|
| `Id` | int PK | |
| `Slug` | varchar(120) unique | URL `/live/{slug}` |
| `Title` | varchar(200) | |
| `Description` | varchar(2000) | |
| `PosterUrl` | varchar(600) | |
| `BackdropUrl` | varchar(600) | |
| `Category` | varchar(64) | Thể thao, Tin tức, Giải trí... |
| `IsActive` | bool | |
| `EpgUrl` | varchar(600) nullable | XMLTV |
| `CreatedAtUtc` | timestamptz | |
| `UpdatedAtUtc` | timestamptz | |
| `RowVersion` | bytea/xmin | Optimistic concurrency |

#### `live_sources`

| Cột | Kiểu | Ghi chú |
|-----|------|---------|
| `Id` | int PK | |
| `ChannelId` | int FK | Cascade |
| `ServerName` | varchar(120) | |
| `ServerType` | int | VIP/CDN/Backup |
| `Protocol` | int | `Hls`, `LlHls`, `Dash`, `WebRtc` |
| `PlaybackUrl` | varchar(600) | URL manifest gốc (mã hoá nếu cần) |
| `Region` | varchar(32) | `HN`, `SG`, `US`... |
| `Priority` | int | 1 = cao nhất |
| `Weight` | int | 1–100 cho load balancing |
| `LatencyProfile` | int | `Standard`, `LowLatency` |
| `DvrWindowMinutes` | int | 0 = không DVR |
| `IsPrimary` | bool | |
| `IsEnabled` | bool | |
| `HealthStatus` | int | `Unknown/Healthy/Degraded/Down` |
| `LastCheckedAtUtc` | timestamptz | |
| `LastLatencyMs` | int | |
| `LastTtfbMs` | int | |
| `LastError` | varchar(500) | |
| `CreatedAtUtc` | timestamptz | |
| `UpdatedAtUtc` | timestamptz | |
| `RowVersion` | bytea/xmin | |

Index: `(ChannelId, IsEnabled, Priority)`, `(HealthStatus)`.

#### `live_programs` (EPG)

| Cột | Kiểu |
|-----|------|
| `Id` | bigint PK |
| `ChannelId` | int FK |
| `StartsAtUtc` | timestamptz |
| `EndsAtUtc` | timestamptz |
| `Title` | varchar(240) |
| `Description` | varchar(2000) |
| `ThumbnailUrl` | varchar(600) |
| `EpgId` | varchar(120) nullable |

Index: `(ChannelId, StartsAtUtc)`.

#### `stream_health_samples`

| Cột | Kiểu |
|-----|------|
| `Id` | bigint PK |
| `SourceKind` | int (`Vod`/`Live`) |
| `SourceId` | varchar(64) |
| `CheckedAtUtc` | timestamptz |
| `IsSuccess` | bool |
| `LatencyMs` | int |
| `TtfbMs` | int |
| `ThroughputKbps` | int |
| `StatusCode` | int |
| `ErrorCode` | varchar(80) |

Retention 30 ngày; partition theo tháng nếu dữ liệu lớn.

#### `playback_sessions`

| Cột | Kiểu |
|-----|------|
| `Id` | uuid PK |
| `UserId` | uuid FK |
| `ContentKind` | int |
| `ContentId` | int |
| `Position` | int |
| `DeviceIdHash` | varchar(64) |
| `IpHash` | varchar(64) |
| `TokenJti` | varchar(64) |
| `CreatedAtUtc` | timestamptz |
| `ExpiresAtUtc` | timestamptz |
| `RevokedAtUtc` | timestamptz nullable |
| `RevokeReason` | varchar(200) |

Có thể tận dụng `ActiveStreams` hiện tại + thêm cột `RevokedAtUtc`/`TokenJti` thay vì bảng mới; tuy nhiên bảng riêng giúp audit và không phá lease.

### B2. Admin workspace `/admin/live`

**Cấu trúc trang**:

```
/admin/live
  ├─ Tab Kênh       : danh sách kênh, trạng thái, viewers, latency, healthy sources
  ├─ Tab Nguồn      : bảng nguồn theo kênh, priority, region, protocol, health, last probe
  ├─ Tab EPG        : timeline 24h, import XMLTV, program list
  └─ Tab Sức khoẻ   : health history, error rate, failover events, alerts
```

**Chức năng nguồn**:

- Thêm/sửa/xoá mềm, nhân bản, bật/tắt.
- Kéo-thả priority; đặt primary; weight.
- Probe ngay; probe tất cả; xem latency/TTFB/throughput.
- Health badge: `Healthy` xanh, `Degraded` vàng, `Down` đỏ, `Unknown` xám.
- Cảnh báo: "Chỉ còn 1 nguồn", "Tất cả nguồn đang Down", "Nguồn chính đang Degraded".
- Test playback bằng token thật (không bypass auth), mở player trong modal.
- Import M3U: parse `#EXTINF`, preview, validate, import hàng loạt.
- Copy URL: chỉ hiện host, không hiện token; nút "Copy manifest URL" yêu cầu quyền admin.

**Guardrails**:

- Không cho xoá nguồn cuối cùng của kênh đang active.
- Disable nguồn primary phải confirm + chọn nguồn thay thế.
- Lưu URL sai/SSRF → hiện lỗi rõ, không lưu.
- Mọi thao tác ghi audit + actor + diff.

### B3. API public & player

**Endpoints**:

| Method | Path | Auth | Mô tả |
|--------|------|------|-------|
| GET | `/api/live/channels` | Public | Danh sách kênh active (cache 30s) |
| GET | `/api/live/channels/{slug}` | Public | Chi tiết kênh + sources healthy |
| GET | `/api/live/channels/{slug}/epg?from&to` | Public | EPG |
| POST | `/api/live/channels/{id}/sessions` | User | Tạo playback session + token |
| GET | `/api/live/sessions/{sid}/manifest` | Token | Manifest (proxy/rewrite hoặc redirect CDN) |
| GET | `/api/live/sessions/{sid}/segment` | Cookie/Token | Segment (nếu proxy) |
| POST | `/api/live/sessions/{sid}/heartbeat` | Token | Heartbeat |
| DELETE | `/api/live/sessions/{sid}` | Token | Kết thúc |
| POST | `/api/playback/telemetry` | Public/User | QoE beacon |

**Player live**:

- hls.js với `lowLatencyMode: true` nếu LL-HLS.
- Hiển thị badge `LIVE`, độ trễ ước tính, nút "Về live edge".
- DVR seek bar nếu `DvrWindowMinutes > 0`.
- Quality selector (ABR levels).
- Auto failover: khi fatal error, thử nguồn kế tiếp healthy; hiển thị "Đang chuyển nguồn...".
- Retry exponential backoff + jitter; max 3 lần.
- PiP/fullscreen giữ nguyên.

### B4. EPG & DVR

- **XMLTV import**: worker tải `EpgUrl` mỗi 15 phút; parse `<programme start stop channel>`; upsert theo `(ChannelId, EpgId)`; timezone `Asia/Ho_Chi_Minh`.
- **EPG UI**: timeline 24h, chương trình hiện tại/sắp tới, click để xem; cache 5 phút.
- **DVR**: lấy `DvrWindowMinutes` từ source; player cho seek trong cửa sổ; nếu nguồn không hỗ trợ, ẩn seek live.
- **Reminder** (optional P2): thông báo trước 5 phút qua SignalR.

### B5. Health monitoring & failover

**Worker** (`StreamHealthMonitor : BackgroundService`):

- Chu kỳ 15s cho mỗi source enabled.
- Với HLS: GET manifest (timeout 5s), parse:
  - Có `#EXTM3U`?
  - Live: không có `#EXT-X-ENDLIST`; `#EXT-X-TARGETDURATION`; segment mới nhất.
  - LL-HLS: có `#EXT-X-PART-INF`, `#EXT-X-PART`.
  - TTFB, status, content-type.
- Với MP4: HEAD hoặc GET Range 0-1023; kiểm tra `Accept-Ranges`.
- Lưu `stream_health_samples`; cập nhật source status.

**Scoring**:

```
score = 0.40 * success_rate_5m
      + 0.25 * clamp(1 - ttfb_ms / 2000)
      + 0.20 * clamp(throughput_kbps / target_bitrate)
      + 0.15 * freshness_score
```

**State machine**:

```
Unknown --success--> Healthy
Healthy --2 fails--> Degraded --1 fail--> Down
Down --2 successes--> Healthy
Degraded --2 successes--> Healthy
```

Cooldown 60s chống flapping; không tự disable source, chỉ đánh dấu Down và loại khỏi selection.

**Failover**:

- Server trả danh sách source theo `Priority`, `HealthStatus`, `Region`.
- Client chọn source đầu; nếu fatal, chuyển source kế tiếp (khác host), giữ vị trí.
- Server có thể pin session vào source để tránh mỗi segment một host.
- Nếu tất cả Down: trả 503 kèm `retryAfter`, hiển thị thông báo.
- Admin nhận alert khi source Down > 2 phút hoặc tất cả Down.

---

## 6. Track C — Tối ưu đường truyền & hiệu năng (P0/P1)

### C1. Đưa CDN/Edge ra trước video

**Mục tiêu**: app không phải là đường ống video.

- **Nginx/Caddy edge** (nếu self-host):
  - `proxy_buffering off` cho live; `proxy_buffering on` cho VOD.
  - `sendfile on`, `tcp_nopush on`, `aio threads`.
  - `limit_conn`/`limit_req` theo IP/session.
  - Cache segment: `proxy_cache` + `proxy_cache_valid 200 1y`.
  - `slice`/range support.
- **CDN** (khuyến nghị): CloudFront/Cloudflare/Fastly/Bunny.
  - Origin shield.
  - Cache segment immutable; manifest TTL 2–6s.
  - Signed cookie/URL ở edge.
  - HTTP/3, TLS 1.3, Brotli cho manifest.
- **Multi-CDN**: primary + backup; health-based steering; player fallback nếu CDN lỗi.

**Cache-Control matrix**:

| Tài nguyên | Cache-Control | Ghi chú |
|------------|---------------|---------|
| Manifest VOD (signed) | `private, no-store` | Không cache |
| Manifest live | `public, max-age=2, stale-while-revalidate=2` | Edge cache ngắn |
| Segment VOD | `public, max-age=31536000, immutable` | Nếu edge auth |
| Segment live | `public, max-age=6` | Ngắn hơn segment duration |
| API JSON | `public, max-age=30` hoặc `no-store` | Theo endpoint |
| Token/session | `no-store` | Tuyệt đối |

### C2. Tối ưu proxy nếu vẫn giữ trong app

Áp dụng ngay cả khi đã có CDN (fallback):

1. **Không buffer toàn bộ**: `HttpCompletionOption.ResponseHeadersRead` (đã có), `Response.BodyWriter`/`CopyToAsync` buffer 64KB.
2. **Tắt buffering của ASP.NET**: `HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering()`.
3. **Truyền header**: `Content-Length`, `Content-Range`, `Accept-Ranges`, `ETag`, `Last-Modified`, `Content-Type`.
4. **Range**:
   - Hỗ trợ single range; trả `206` + `Content-Range`.
   - `416` nếu range invalid.
   - Không hỗ trợ multi-range (trả 200 hoặc 416) để tránh phức tạp.
5. **HEAD**: trả metadata không body.
6. **Timeout & cancellation**:
   - Connect 5s, header 10s, idle read 30s.
   - `CancellationToken` từ client; không log `TaskCanceledException` là lỗi.
7. **Connection pooling**:
   - `MaxConnectionsPerServer = 64` (hoặc 128).
   - `EnableMultipleHttp2Connections = true`.
   - `PooledConnectionLifetime = 5m`.
   - `KeepAlivePingDelay = 30s`.
8. **Redirect**: tối đa 3, validate từng hop.
9. **Circuit breaker**: Polly per origin; 5 lỗi liên tiếp → open 30s; fallback source kế tiếp.
10. **Zero-copy với Nginx**: trả `X-Accel-Redirect` tới internal location; Nginx serve file/upstream.

### C3. HLS/LL-HLS & ABR

**Packaging**:

- VOD: 2s segment, 4s target duration, 4–6 renditions (240p→4K tuỳ nội dung).
- Live chuẩn: 2–4s segment, latency 8–15s.
- LL-HLS: 0.5s part, hold-back 1.5–3s, latency 3–5s.
- Audio riêng (AAC 128k), subtitle WebVTT.

**hls.js config đề xuất**:

```js
{
  enableWorker: true,
  lowLatencyMode: true,          // chỉ bật khi LL-HLS
  backBufferLength: 30,
  maxBufferLength: 30,
  maxMaxBufferLength: 60,
  liveSyncDurationCount: 2,
  liveMaxLatencyDurationCount: 5,
  maxLiveSyncPlaybackRate: 1.5,
  capLevelToPlayerSize: true,
  startLevel: -1,
  abrEwmaDefaultEstimate: 800000,
  testBandwidth: true
}
```

- VOD: `lowLatencyMode: false`, `maxBufferLength: 60`.
- Mobile: cap 720p mặc định; desktop cap theo player size.
- Prefetch: manifest + first segment của tập kế tiếp khi gần hết; `<link rel="preconnect">` tới CDN.
- Fallback: Safari native HLS; hls.js cho browser khác; DASH dùng Shaka nếu cần.

### C4. Nén & giao thức

- **Không nén video/segment** (đã là nén).
- Nén JSON/manifest bằng Brotli/Gzip; `Vary: Accept-Encoding`.
- HTTP/2 cho API; HTTP/3/QUIC cho CDN nếu có.
- TLS 1.3, OCSP stapling, session resumption.
- DNS preconnect, keep-alive, TCP fast open (nếu hỗ trợ).
- Giảm round-trip: cache catalog, ETag, conditional GET.

### C5. Client prefetch & failover

- `preload="metadata"` (đã có); thêm preload manifest khi hover poster/tập.
- Lưu `deviceId`, `lastQuality`, `lastServer` vào localStorage; tôn trọng preference nhưng không vượt quá khả năng mạng.
- Telemetry beacon (xem mục 7).
- Auto failover: thử tối đa 2 nguồn khác; nếu tất cả lỗi, hiện nút "Báo lỗi" + mã lỗi.
- Hiển thị trạng thái: `Đang tải`, `Đang chuyển nguồn`, `Mạng yếu — giảm chất lượng`.

---

## 7. Observability & SLO

### 7.1. Metrics

| Nhóm | Metric | Mô tả |
|------|--------|-------|
| Playback | `playback_session_started_total` | Theo content/source/tier |
| Playback | `playback_start_seconds` | Từ bấm play → first frame |
| Playback | `playback_rebuffer_ratio` | Thời gian rebuffer / thời gian xem |
| Playback | `playback_error_total` | Theo mã lỗi/source |
| Playback | `playback_bitrate_kbps` | Bitrate thực tế |
| Delivery | `stream_manifest_ttfb_ms` | TTFB manifest |
| Delivery | `stream_segment_ttfb_ms` | TTFB segment |
| Delivery | `stream_bytes_total` | Egress theo source |
| Delivery | `cdn_cache_hit_ratio` | Tỷ lệ hit |
| Health | `stream_source_health` | 1 healthy / 0 down |
| Health | `stream_source_latency_ms` | Latency probe |
| Security | `stream_token_rejected_total` | Theo lý do |
| Security | `stream_session_revoked_total` | |
| Security | `stream_ssrf_blocked_total` | |

### 7.2. SLO đề xuất

| SLO | Mục tiêu | Cửa sổ |
|-----|----------|--------|
| Playback success rate | ≥ 99.5% | 7 ngày |
| Startup p95 | ≤ 2s (10 Mbps) | 7 ngày |
| Rebuffer ratio | ≤ 1% | 7 ngày |
| Live latency p95 | ≤ 5s (LL-HLS) | 7 ngày |
| Failover time | ≤ 5s | 7 ngày |
| Source down detection | ≤ 60s | 7 ngày |
| CDN hit ratio | ≥ 95% | 7 ngày |
| Origin egress | ≤ 20% tổng bytes | 7 ngày |
| Token rejection false positive | ≤ 0.1% | 7 ngày |

### 7.3. Dashboards & alerts

- **Playback health**: success, startup, rebuffer, errors theo source/region/device.
- **Delivery**: CDN hit, origin egress, TTFB, HTTP status.
- **Source health**: trạng thái, latency, failover events.
- **Security**: token rejected, SSRF blocked, concurrent limit hits, admin actions.
- **Alerts**:
  - Source Down > 2 phút.
  - Tất cả source của kênh Down.
  - Playback error rate > 5% trong 5 phút.
  - Rebuffer ratio > 3% trong 10 phút.
  - Origin egress tăng > 50% so với baseline.
  - 401/403 tăng đột biến.

### 7.4. Tracing & logging

- OpenTelemetry: trace `playback_session → manifest → segment`; span tags `contentId`, `sourceId`, `region`, `status`.
- Log structured JSON; correlation `sessionId`; redact token/cookie/IP.
- Sampling: 10% trace thường, 100% lỗi.

---

## 8. Kiểm thử & kiểm chứng

### 8.1. Ma trận test

| Loại | Công cụ | Cases chính |
|------|---------|-------------|
| Unit | xUnit | Token sign/validate, expiry, skew, kid rotation, IP/device mismatch, SSRF matrix, health scoring, failover ordering |
| Integration | WebApplicationFactory | Admin CRUD + audit, unauthorized, VIP gating, lease/concurrency, rate limit, CORS, headers |
| Component | bUnit | Admin live page, player states, error/failover UI |
| E2E | Playwright (optional) | Login → watch → switch source → expire token → re-auth |
| Load | k6/Artillery | 1k/5k concurrent, 4K bitrate, soak 24h |
| Security | OWASP ZAP, dependency scan, secret scan | SSRF, IDOR, token replay, rate limit bypass, header injection |
| Chaos | Toxiproxy/script | Origin 500, timeout, 1 Mbps, CDN 403, stale manifest, DNS fail |

### 8.2. Test bắt buộc trước khi release

- [ ] Không thể lưu `http://169.254.169.254`, `http://10.0.0.1`, `http://localhost`, IPv6 loopback.
- [ ] Redirect từ URL công khai sang private bị chặn.
- [ ] Token hết hạn bị 403; token sai chữ ký bị 403; token đúng nhưng session revoked bị 403.
- [ ] Token từ IP khác bị 403 (hoặc yêu cầu re-auth theo policy).
- [ ] Concurrent limit đúng theo hạng; evict gửi SignalR.
- [ ] HLS manifest được rewrite đúng; segment không trỏ origin.
- [ ] Range request trả 206 đúng; seek hoạt động.
- [ ] Rate limit segment không chặn oan người xem bình thường.
- [ ] Failover tự động khi source chính 500/timeout.
- [ ] Publish RTMP/SRT với stream key sai bị từ chối; key rotate không làm hỏng phiên đang phát.
- [ ] Hook `runOnReady`/`runOnNotReady` sai HMAC hoặc timestamp cũ bị từ chối.
- [ ] Client không thể truy cập trực tiếp origin MediaMTX; mọi request phải qua edge.
- [ ] Manifest live không lộ `originBaseUrl`/stream key; segment chỉ phục vụ qua edge có auth.
- [ ] LL-HLS part request trả đúng `Content-Type`, latency đo được ≤ 5s.
- [ ] Không log token/URL gốc/stream key.
- [ ] `dotnet build` 0 warning/0 error; `dotnet test` 100% pass.

---

## 9. Roadmap & ước lượng

> Ước lượng cho 1 dev full-stack + 1 reviewer; thời gian có thể co giãn theo hạ tầng, số kênh live và mức độ hoàn thiện của edge/MediaMTX.

### Phase 0 — Quick wins & guardrails (3–5 ngày)

| ID | Việc | File chính | Ước lượng | Exit criteria |
|----|------|------------|-----------|---------------|
| P0-01 | Validate SSRF khi add/update stream | `AdminEpisodesController.cs` | 0.5 ngày | Lưu URL nội bộ bị 400; test pass |
| P0-02 | Giảm TTL token + thêm `kid/jti/sid/scope` | `StreamUrlSigner.cs`, `appsettings.json` | 1 ngày | Token VOD 10 phút / Live 5 phút; rotate key không downtime |
| P0-03 | Rate limit riêng cho playback | `ApiRateLimits.cs`, `StreamController.cs` | 0.5 ngày | Segment không bị 429 oan; có metric |
| P0-04 | Kiểm tra lease/session trong `/play` | `StreamController.cs` | 1 ngày | Session revoked → 403 |
| P0-05 | Sửa `ActiveStreamsCount`, max theo tier | `StreamSessionService.cs` | 1 ngày | Trả đúng số; test concurrency |
| P0-06 | Health badge + validate button trong admin | `AdminEpisodes.razor` | 1 ngày | Admin thấy trạng thái/last probe |
| P0-07 | Telemetry endpoint + metric cơ bản | Controller mới | 1 ngày | Có dashboard thô |

**Trạng thái triển khai Phase 0 (2026-10-25):**

- P0-01 → P0-05: đã hoàn tất. SSRF validation cho add/update stream và episode/batch; token VOD 10 phút với `sid/scope/kid`, key ring xoay khóa không downtime, clock skew 30s; rate limit riêng `StreamPlayback`/`StreamSign`; `/play` kiểm tra lease còn sống + đúng tập + đúng scope; `ActiveStreamsCount` và giới hạn đồng thời theo tier đã trả đúng.
- P0-06: UI probe/ping từng nguồn và "Ping tất cả" đã có trong modal quản lý luồng; badge health/last-probe lưu bền sẽ đi cùng migration `stream_health_samples` ở Phase 2.
- P0-07: endpoint `GET /api/admin/system/health` hiện có đã phủ health cơ bản; dashboard telemetry QoE/playback chuyên biệt nằm trong Phase 1 (P1-06).
- Sửa kèm: `CinemaHub` được đánh dấu `[ActivatorUtilitiesConstructor]` để SignalR không còn lỗi "Multiple constructors accepting all given argument types" khi khởi tạo hub — lỗi này chặn luồng cấp lease và làm URL không có `sid`.
- Kiểm chứng: build 0 warning/0 error; toàn bộ test pass; đã xác minh live trên Docker: user VIP nhận URL có `sid` + `scope=play`, lease `active_streams` còn sống, video phát bình thường.

### Phase 1 — Bảo mật & delivery VOD (1–2 tuần)

| ID | Việc | Ước lượng | Exit criteria |
|----|------|-----------|---------------|
| P1-01 | Playback session API + token refresh/revoke | 2 ngày | Luồng session đầy đủ |
| P1-02 | HLS manifest rewrite/proxy hoặc chuyển CDN | 2–3 ngày | Segment không lộ origin |
| P1-03 | Cookie HttpOnly cho segment + edge auth | 1–2 ngày | Không token trong query/log |
| P1-04 | Proxy optimization (range, buffer, timeout, pool) | 1–2 ngày | p95 TTFB giảm; seek ổn |
| P1-05 | HLS.js ABR/low-latency config + auto failover | 1–2 ngày | Failover < 5s |
| P1-06 | QoE telemetry đầy đủ + dashboard | 2 ngày | Có startup/rebuffer/error |
| P1-07 | Test matrix + load test 1k | 2 ngày | 0 lỗi nghiêm trọng |

**Trạng thái triển khai Phase 1 (2026-10-25):**

- P1-01: đã hoàn tất. Thêm `POST /api/stream/sessions/{sid}/refresh` (chỉ chủ sở hữu lease, đúng tập) và `POST /api/stream/sessions/{sid}/revoke` (idempotent, xoá cookie phiên). Client tự refresh ở 70% TTL; nếu trình duyệt chặn cookie thì fallback đổi URL và resume đúng vị trí; rời trang thu hồi qua API, lỗi mạng mới fallback SignalR.
- P1-03: đã có phần cookie playback `cf_play` (HttpOnly, `SameSite=Lax`, path `/api/stream`, HMAC gắn `sid` + tập + IP). `/play` chấp nhận URL đã hết hạn khi cookie còn sống và lease chưa bị thu hồi, đồng thời gia hạn cookie theo từng request media — nhờ vậy không phải đổi `src` của video. Phần `auth_request`/edge cache vẫn chờ hạ tầng edge.
- Kiểm chứng: build 0 warning/0 error; 1286/1286 test pass, gồm refresh thành công, refresh sai người dùng, refresh sau revoke, revoke idempotent, cookie hợp lệ với URL hết hạn, cookie hết hạn/bị sửa, và lease đã thu hồi.

### Phase 2 — Live MVP với MediaMTX (3–4 tuần)

> Phương án B đã chốt: MediaMTX + ffmpeg + (tùy chọn) Shaka Packager chạy như dịch vụ riêng. API .NET không transcode.

| ID | Việc | Ước lượng | Exit criteria |
|----|------|-----------|---------------|
| P2-01 | Hạ tầng MediaMTX: Docker Compose, `mediamtx.yml`, volume HLS/recordings, healthcheck, log rotation | 3 ngày | Publish thử RTMP/SRT thành công; restart không mất cấu hình |
| P2-02 | Publish auth hook + stream key hash/rotate: `POST /api/internal/live/authorize`, HMAC hook `runOnReady` | 2–3 ngày | Key sai bị 401; key rotate không downtime; hook chống replay |
| P2-03 | Transcode ladder ffmpeg + LL-HLS: 240p→1080p, keyframe 2s, part 0.5s cho kênh LL | 3–4 ngày | Player phát đủ rendition; latency đo được 3–5s (LL) / 8–15s (chuẩn) |
| P2-04 | Migration `live_channels/live_sources/live_programs/stream_health_samples` + stream key fields | 1.5 ngày | Migration up/down sạch; backfill an toàn |
| P2-05 | Admin `/admin/live`: CRUD kênh/nguồn, ingest credentials, priority, region, probe, health badge | 5 ngày | Thêm/sửa/xoá/probe/priority; không lộ stream key sau khi tạo |
| P2-06 | Public live API + EPG (`/api/live/channels`, `/epg`) | 2 ngày | Danh sách/chi tiết/EPG; cache 30s |
| P2-07 | Live session/token + player LL-HLS + DVR seek + badge LIVE | 4 ngày | Xem live 3–5s latency; DVR hoạt động theo `DvrWindowMinutes` |
| P2-08 | Health monitor worker + failover + alert: probe manifest/part, state machine, chuyển nguồn | 3 ngày | Down detect < 60s; failover < 5s; không flapping |
| P2-09 | XMLTV import + DVR retention/cleanup job | 2 ngày | EPG hiển thị đúng timezone; file cũ tự dọn |
| P2-10 | Nginx/Caddy edge: cache segment, `auth_request`, HTTP/2, TLS, rate limit | 2–3 ngày | Origin egress giảm rõ rệt; segment không lộ MediaMTX |
| P2-11 | Audit/guardrails/admin tests + load test live 500 concurrent | 3 ngày | Không xoá nguồn cuối; 0 lỗi nghiêm trọng; SLO latency đạt |

### Phase 3 — Scale & advanced (3–5 tuần)

| ID | Việc | Ước lượng | Exit criteria |
|----|------|-----------|---------------|
| P3-01 | CDN/multi-CDN + edge auth | 1–2 tuần | Origin egress < 20% |
| P3-02 | Redis session/cache cho multi-instance | 3–5 ngày | 2 instance không race |
| P3-03 | Forensic watermark server-side | 1–2 tuần | Truy vết được session |
| P3-04 | DRM/AES-128 + license proxy | 2–4 tuần | Phát trên Chrome/Safari |
| P3-05 | Load/chaos 5k + soak | 1 tuần | SLO đạt |
| P3-06 | Runbook/alert/on-call | 3 ngày | Có quy trình xử lý |

---

## 10. API contracts chi tiết

### 10.1. Tạo playback session

```http
POST /api/stream/sessions
Authorization: Bearer <access_token>
Content-Type: application/json

{
  "contentKind": "episode",     // episode | live
  "episodeId": 123,
  "position": 2,
  "deviceId": "dev_abc123",
  "sessionId": null
}
```

Response `200`:

```json
{
  "success": true,
  "data": {
    "sessionId": "8f3c2a...",
    "token": "v1.eyJ...",
    "manifestUrl": "/api/stream/sessions/8f3c2a.../manifest",
    "expiresAt": "2026-10-25T10:10:00Z",
    "refreshAfterSeconds": 420,
    "heartbeatIntervalSeconds": 30,
    "lease": {
      "leaseId": "…",
      "activeStreams": 1,
      "maxStreams": 3
    }
  }
}
```

Lỗi:

| HTTP | Mã | Khi nào |
|------|----|---------|
| 401 | `AUTH_REQUIRED` | Chưa đăng nhập |
| 403 | `VIP_REQUIRED` | Nguồn VIP/4K |
| 403 | `CONCURRENT_LIMIT` | Vượt số phiên |
| 404 | `SOURCE_NOT_FOUND` | Không có nguồn |
| 409 | `SOURCE_UNHEALTHY` | Tất cả nguồn Down |
| 429 | `RATE_LIMITED` | Quá nhiều request |
| 400 | `UNSAFE_URL` | URL SSRF/không hợp lệ |

### 10.2. Manifest/segment

```http
GET /api/stream/sessions/{sid}/manifest
Cookie: cf_play=<token>
```

- `200` `application/vnd.apple.mpegurl` (đã rewrite) + `Set-Cookie`.
- `206` cho segment range.
- `403` token invalid/expired/revoked.
- `410` session ended.

### 10.3. Admin live source

```http
POST /api/admin/live/channels/{channelId}/sources
Authorization: Bearer <admin_token>
```

```json
{
  "serverName": "VIP HN 1",
  "serverType": "VipFastServer",
  "protocol": "LlHls",
  "playbackUrl": "https://origin.example.com/live/ch1/master.m3u8",
  "region": "HN",
  "priority": 1,
  "weight": 100,
  "latencyProfile": "LowLatency",
  "dvrWindowMinutes": 60,
  "isPrimary": true,
  "isEnabled": true
}
```

Validation:

- URL safe (SSRF), HTTPS production.
- Protocol enum hợp lệ.
- Priority 1–100, weight 1–100.
- Không trùng `(ChannelId, PlaybackUrl)`.
- Nếu `isPrimary` → bỏ primary cũ trong transaction.

### 10.4. Telemetry

```http
POST /api/playback/telemetry
Content-Type: application/json

{
  "sessionId": "8f3c2a...",
  "contentKind": "episode",
  "contentId": 123,
  "sourceId": 2,
  "event": "rebuffer_end",
  "value": 1.8,
  "bitrateKbps": 4500,
  "errorCode": null,
  "clientTs": "2026-10-25T10:00:00Z"
}
```

- Batch tối đa 20 event/request.
- Không nhận PII; `sessionId` đã đủ.
- Rate limit 60 request/phút/session.
- Trả `204`.

### 10.5. Internal — MediaMTX publish auth & hook

```http
POST /api/internal/live/authorize
X-Mtx-Secret: <HMAC(timestamp + body, MEDIAMTX_HOOK_SECRET)>
X-Mtx-Timestamp: 1760000000

{
  "path": "live/channel-slug",
  "action": "publish",
  "protocol": "rtmp",
  "user": "channel-slug",
  "password": "<stream-key>",
  "ip": "203.0.113.10"
}
```

- Chỉ nhận từ private network; nếu không phải loopback/private → `403`.
- Kiểm tra channel `IsActive`, stream key hash khớp, IP nằm trong allowlist (nếu cấu hình).
- `200` kèm `{ "allowed": true, "channelId": 45 }`; từ chối trả `401`/`403`.
- Hook `runOnReady`/`runOnNotReady` dùng cùng cơ chế HMAC + timestamp (skew ≤ 60s), body chứa `path`, `state`, `bytesReceived`, `tracks`.
- Không log stream key; chỉ log `channelId`, `action`, `result`, `ipHash`.

---

## 11. Migration kế hoạch

### 11.1. Thứ tự migration

1. `AddPlaybackSessionFields` — thêm `TokenJti`, `RevokedAtUtc`, `RevokeReason`, `DeviceIdHash`, `IpHash` vào `active_streams` (hoặc bảng mới).
2. `AddLiveChannels` — bảng `live_channels`.
3. `AddLiveSources` — bảng `live_sources` + index.
4. `AddLivePrograms` — bảng `live_programs` + index.
5. `AddStreamHealthSamples` — bảng + index + retention job.
6. `AddStreamSourceHealthFields` — thêm `Protocol`, `Priority`, `Region`, `IsEnabled`, `HealthStatus`, `LastCheckedAtUtc`, `LastLatencyMs`, `LastError` vào `stream_sources` (VOD).
7. `AddStreamSourceRowVersion` — optimistic concurrency.

### 11.2. Backfill

- `Protocol` mặc định `Hls` nếu URL chứa `.m3u8`, ngược lại `Mp4`.
- `Priority` = `Position`.
- `IsEnabled` = true.
- `HealthStatus` = Unknown.
- Không đổi `StreamUrl` hiện có.

### 11.3. Rollback

- Mỗi migration có `Down` tương ứng.
- Không xoá cột cũ ngay; giữ 1 release.
- Feature flag `StreamSecurity:UsePlaybackSessions` để tắt nhanh nếu sự cố.

---

## 12. Cấu hình đề xuất

### 12.1. `appsettings.json` (server)

```json
{
  "Security": {
    "StreamTtlMinutes": 10,
    "StreamSigningKeys": [
      { "Kid": "2026-10", "Secret": "" }
    ],
    "ActiveStreamKid": "2026-10",
    "StreamTokenClockSkewSeconds": 30,
    "RateLimits": {
      "ApiPerMinute": 300,
      "AuthPerMinute": 15,
      "StreamPlaybackPerMinute": 600,
      "StreamManifestPerMinute": 60,
      "TelemetryPerMinute": 60
    }
  },
  "StreamSecurity": {
    "UsePlaybackSessions": true,
    "RequireSessionBinding": true,
    "AllowIpChangeWithinAsn": true,
    "DefaultMaxStreams": 1,
    "LiveTokenTtlMinutes": 5,
    "TokenRefreshAtPercent": 70,
    "MaxConcurrentStreams": {
      "Free": 1,
      "Silver": 2,
      "Gold": 3,
      "Platinum": 4,
      "Diamond": 4,
      "Admin": 5
    }
  },
  "StreamDelivery": {
    "Mode": "Edge",
    "CdnBaseUrl": "",
    "EdgeBaseUrl": "https://edge.local",
    "OriginBaseUrl": "http://mediamtx:8888",
    "ManifestCacheSeconds": 4,
    "SegmentCacheSeconds": 31536000,
    "EnableHttp3": false,
    "ProxyBufferBytes": 65536,
    "UpstreamConnectTimeoutSeconds": 5,
    "UpstreamHeaderTimeoutSeconds": 10,
    "UpstreamIdleTimeoutSeconds": 30
  },
  "Live": {
    "HealthCheckIntervalSeconds": 15,
    "DegradedAfterFailures": 2,
    "DownAfterFailures": 3,
    "RecoverySuccesses": 2,
    "EpgRefreshMinutes": 15,
    "HealthRetentionDays": 30,
    "MediaMtx": {
      "ApiBaseUrl": "http://mediamtx:9997",
      "HlsBaseUrl": "http://mediamtx:8888",
      "RtmpPort": 1935,
      "SrtPort": 8890,
      "PublishAuthUrl": "http://api:8080/api/internal/live/authorize",
      "HookSecret": "",
      "DefaultDvrWindowMinutes": 60,
      "RecordingEnabled": false,
      "LowLatencyPartSeconds": 0.5,
      "LowLatencyHoldBackSeconds": 2,
      "SegmentSeconds": 2
    }
  }
}
```

### 12.2. Biến môi trường production

| Biến | Bắt buộc | Ghi chú |
|------|----------|---------|
| `STREAM_SIGNING_KEY` | Có (hoặc key ring) | >= 32 ký tự, không default |
| `STREAM_SIGNING_KID` | Có | |
| `MEDIAMTX_HOOK_SECRET` | Có (khi bật live) | HMAC cho `runOnReady`/`runOnNotReady` |
| `MEDIAMTX_PUBLISH_AUTH_URL` | Có (khi bật live) | Trỏ tới API internal, chỉ trong private network |
| `MEDIAMTX_SRT_PASSPHRASE` | Nếu dùng SRT | Có thể override theo từng kênh |
| `EDGE_BASE_URL` | Có | URL công khai của Nginx/Caddy edge |
| `CDN_SIGNING_KEY` / `CDN_BASE_URL` | Phase 3 | Khi thay edge bằng CDN thật |
| `REDIS_CONNECTION` | Multi-instance | Session/cache |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Khuyến nghị | Observability |

---

## 13. Rủi ro & giảm thiểu

| Rủi ro | Xác suất | Tác động | Giảm thiểu |
|--------|----------|----------|------------|
| HLS rewrite phức tạp, sai sót | Cao | Cao | Ưu tiên CDN edge; nếu tự proxy, viết parser/test kỹ; fallback MP4 |
| CDN tốn chi phí | Trung bình | Trung bình | So sánh giá; cache hit cao; multi-CDN; giới hạn bitrate |
| DRM license phức tạp | Trung bình | Cao | Dùng giải pháp có sẵn; làm P2 sau khi ổn định |
| Live transcoding tốn CPU/GPU | Cao | Cao | Đã chọn B: đo tải trước, giới hạn số kênh/instance, dùng NVENC/QSV khi scale; fallback A/C cho sự kiện lớn |
| MediaMTX single point of failure | Trung bình | Cao | Healthcheck + restart policy; encoder dự phòng; API đánh dấu Down và chuyển nguồn; runbook khôi phục |
| Stream key ingest bị lộ | Trung bình | Cao | Hash + pepper, rotate, IP allowlist, SRT passphrase, không log key |
| Edge cache làm lộ segment ngoài phiên | Trung bình | Cao | `auth_request` trước cache; token ngắn; không cache manifest có token; purge khi revoke |
| IP binding chặn oan mạng di động | Trung bình | Trung bình | Cho phép đổi IP trong cùng ASN; re-auth thay vì chặn cứng |
| Rate limit segment chặn oan | Trung bình | Trung bình | Policy riêng theo session; token bucket; theo dõi 429 |
| Multi-instance race lease | Trung bình | Trung bình | Redis/advisory lock; test 2 instance |
| Token rò rỉ qua log/referrer | Trung bình | Cao | Cookie HttpOnly; `Referrer-Policy`; redact log |
| Nguồn live thay đổi URL liên tục | Cao | Trung bình | Health monitor + admin alert; cho phép nhiều nguồn |
| Nội dung bản quyền | Trung bình | Cao | DRM/watermark; tuân thủ pháp lý |

---

## 14. Definition of Done

Một hạng mục chỉ được coi là xong khi:

- [ ] Code + migration + test + tài liệu cập nhật.
- [ ] `dotnet build WebXemPhim.slnx` 0 warning/0 error.
- [ ] `dotnet test` 100% pass (không giảm số test).
- [ ] Có metric/log chứng minh thay đổi (trước/sau).
- [ ] Không log token/URL gốc/IP đầy đủ.
- [ ] Admin có audit cho thao tác nhạy cảm.
- [ ] Có rollback/feature flag.
- [ ] Đã kiểm tra light/dark mode cho UI mới.
- [ ] Đã kiểm tra mobile (360px) và desktop.
- [ ] Có runbook/alert nếu là thay đổi vận hành.

---

## 15. Checklist sprint đầu tiên (file-level)

Thứ tự thực hiện đề xuất:

1. **`src/Server/.../Controllers/Admin/AdminEpisodesController.cs`**
   - Gọi `SafeStreamUrlValidator.ValidateUrlAsync` trong `AddStreamSource`/`UpdateStreamSource`.
   - Trả `400` với `UNSAFE_URL`; không echo URL đầy đủ.

2. **`src/Shared/.../Models/AdminDtos.cs`**
   - Thêm `Protocol`, `Priority`, `Region`, `IsEnabled` vào `UpsertStreamSourceRequest` (optional, default an toàn).
   - Thêm validation range/enum.

3. **`src/Server/.../Security/StreamUrlSigner.cs`**
   - Thêm `kid`, `jti`, `sid`, `scope`, `dev`.
   - Hỗ trợ key ring + rotate.
   - TTL mặc định VOD 10 phút; Live 5 phút (truyền TTL theo loại nội dung).

4. **`src/Server/.../Controllers/StreamController.cs`**
   - Thêm `[EnableRateLimiting("StreamPlayback")]`.
   - Inject `IStreamSessionService`; validate session/lease trước khi proxy.
   - Phát hiện HLS: nếu chưa hỗ trợ rewrite, trả `415` rõ ràng thay vì proxy sai.
   - Thêm HEAD, range chuẩn, timeout, cancellation.

5. **`src/Server/.../Security/ApiRateLimits.cs`**
   - Thêm policy `StreamPlayback`, `StreamManifest`, `Telemetry`.
   - Phân vùng theo `sessionId` (nếu có) hoặc user/IP.
   - Tách playback khỏi global limiter.

6. **`src/Server/.../Services/StreamSessionService.cs`**
   - Max concurrent theo `MembershipTier`.
   - `ActiveStreamsCount` đúng.
   - Chuẩn bị interface cho Redis/advisory lock.

7. **`src/Server/.../Services/StreamHealthMonitor.cs`** (mới)
   - `BackgroundService` probe định kỳ.
   - Lưu sample + cập nhật trạng thái.
   - Chấm điểm + failover ordering.

8. **`src/Server/.../Controllers/PlaybackTelemetryController.cs`** (mới)
   - Nhận batch event, validate session, lưu metric.
   - Rate limit riêng.

9. **`src/Client/.../Components/Pages/Admin/AdminEpisodes.razor`**
   - Health badge, last probe, nút Validate, priority/region (nếu có).
   - Cảnh báo khi lưu URL unsafe.

10. **`src/Client/.../wwwroot/js/cinema-player.js`**
    - HLS config LL/ABR.
    - Telemetry beacon.
    - Auto failover + backoff.
    - Không log URL/token.

11. **`src/Server/.../appsettings.json` + `appsettings.Development.json`**
    - Cấu hình mới; default an toàn.
    - Production validation cho key ring.

12. **Tests**
    - `StreamSecurityTests`, `AdminControllerTests`, `ChallengerM*` bổ sung cases.
    - `ApiIntegrationTests` cho session/lease/rate limit.
    - `ClientTests` cho player failover/telemetry.

### Sprint 2 (Live infra — sau khi Phase 0/1 xong)

13. **`infra/mediamtx/mediamtx.yml` + `docker-compose.live.yml`** (mới)
    - RTMP 1935, SRT 8890, HLS 8888, API 9997.
    - `authMethod: http`, `runOnReady`/`runOnNotReady` hook.
    - Volume `hls/`, `recordings/`; healthcheck `/v3/paths/list`.
    - Resource limits + log rotation.

14. **`infra/edge/nginx.conf` hoặc `Caddyfile`** (mới)
    - `auth_request` tới API; cache segment; manifest TTL 2–6s.
    - Chỉ proxy HLS từ MediaMTX; chặn truy cập trực tiếp origin.
    - TLS 1.3, HTTP/2, Brotli cho manifest.

15. **`src/Server/.../Controllers/Internal/LiveIngestController.cs`** (mới)
    - `POST /api/internal/live/authorize` (publish auth).
    - `POST /api/internal/live/hooks` (ready/not-ready).
    - HMAC + timestamp + IP allowlist; không log stream key.

16. **`src/Server/.../Services/MediaMtxClient.cs`** (mới)
    - Gọi API MediaMTX: list paths, kick publisher, health.
    - Timeout/circuit breaker; không lộ credential ra log.

---

## 16. Quyết định đã chốt (2026-10-25)

| # | Quyết định | Lựa chọn | Hệ quả triển khai |
|---|------------|----------|-------------------|
| 1 | Nguồn Live | **B. Tự host MediaMTX** (RTMP/SRT ingest + ffmpeg + LL-HLS) | Thêm hạ tầng media riêng (mục 3.3); Phase 2 tăng lên 3–4 tuần; cần GPU/CPU và runbook vận hành |
| 2 | CDN | **Chưa có — dùng Nginx/Caddy edge trước** | Phase 1 dựng edge cache + `auth_request`; thiết kế sẵn interface để thay CDN thật ở Phase 3 |
| 3 | DRM | **Chưa bắt buộc — watermark + token ngắn hạn trước** | DRM/AES-128 để Phase 3; P1 tập trung token/session/watermark/device limit |
| 4 | Đồng thời & thiết bị | **Free 1 / Silver 2 / Gold 3 / Platinum 4 / Diamond 4; Admin 5** | Cấu hình theo `MembershipTier`; cho phép đổi IP trong cùng ASN, khác ASN yêu cầu re-auth |
| 5 | Token TTL | **VOD 10 phút, Live 5 phút; refresh ở 70% TTL** | `StreamTtlMinutes: 10`, `LiveTokenTtlMinutes: 5`; clock skew 30s; key ring + rotate |

**Ghi chú từ quyết định**:

- Vì chọn MediaMTX, cần bổ sung **hạng mục hạ tầng** vào Definition of Done: container resource limits, healthcheck, backup config, log rotation, alert khi path không ready.
- Vì chưa có CDN, **không được** để client gọi thẳng MediaMTX. Mọi manifest/segment phải đi qua edge có auth; địa chỉ origin chỉ nằm trong private network.
- Vì DRM để sau, cần **giới hạn kỳ vọng**: token + watermark chỉ giảm chia sẻ, không chống được tải lậu quyết tâm. Khi có nội dung bản quyền lớn, nâng ưu tiên DRM lên Phase 3 sớm.
- Bảng concurrency là mặc định; có thể override theo từng tài khoản qua admin khi cần (ví dụ tài khoản test, sự kiện đặc biệt).

---

## 17. Kết luận

- **Làm ngay (P0)**: SSRF validation, token ngắn hạn + session binding, rate limit playback riêng, lease check, sửa HLS proxy/rewrite.
- **Làm tiếp (P1)**: playback session API, HLS rewrite/edge auth, Nginx/Caddy edge, proxy optimization, telemetry QoE.
- **Live MVP (P2)**: MediaMTX ingest + publish auth, transcode ladder/LL-HLS, domain model + admin `/admin/live`, EPG/DVR, health monitor/failover.
- **Làm sau (P3)**: DRM/AES-128, forensic watermark server-side, CDN/multi-CDN thật, Redis multi-instance, load/chaos 5k.

Nếu chỉ chọn một việc để bắt đầu: **chặn SSRF khi lưu URL + đưa token về 10 phút gắn session + tách rate limit segment**. Ba việc này giảm rủi ro lớn nhất với chi phí thấp nhất, và không phá vỡ kiến trúc hiện tại.

Với hướng MediaMTX đã chốt, nguyên tắc bất di bất dịch là: **ASP.NET Core chỉ là control plane** — không proxy/transcode video ở quy mô lớn; mọi byte hình ảnh đi qua edge/CDN, origin MediaMTX nằm trong private network.
