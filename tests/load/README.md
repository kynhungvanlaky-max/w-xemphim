# Load test luồng phát (P1-07)

Script k6 kiểm tra manifest, segment HLS và Range request của VOD. Mặc định chạy
theo kịch bản ramp tới **1.000 VU** trong 9 phút.

## 1. Chuẩn bị

- Cài [k6](https://k6.io/docs/get-started/installation/).
- Tạo pool tài khoản thử nghiệm. Mỗi tài khoản bị giới hạn phiên đồng thời theo
  gói (`Free=1`, `VIP=2`, `Premium=4`), nên 1.000 VU cần pool tương ứng **hoặc**
  dùng một tài khoản cho tất cả VU khi chỉ đo năng lực edge/segment (các VU cùng
  IP của máy chạy load test).
- Đảm bảo episode được chọn có nguồn HLS hoặc MP4 công khai, không chặn IP
  load generator.

## 2. Chạy nhanh

```powershell
k6 run `
  -e BASE_URL=http://localhost:5085 `
  -e EMAIL=demo@cinemaflow.vn `
  -e PASSWORD=Password123! `
  -e EPISODE_ID=1 `
  -e POSITION=1 `
  -e VUS=50 `
  tests/load/stream-playback.js
```

## 3. Chạy 1.000 VU với pool tài khoản

```powershell
k6 run `
  -e BASE_URL=https://cinemaflow.vn `
  -e ACCOUNTS="load01@test.vn:Password123!,load02@test.vn:Password123!" `
  -e EPISODE_ID=1 `
  -e POSITION=1 `
  -e VUS=1000 `
  tests/load/stream-playback.js
```

`ACCOUNTS` là danh sách `email:password` phân tách bằng dấu phẩy. VU thứ `n` dùng
tài khoản thứ `(n - 1) % số tài khoản`.

## 4. Ngưỡng đạt

| Metric | Ngưỡng |
|---|---|
| `http_req_failed` | < 1% |
| `http_req_duration` p95 | < 1.200 ms |
| `stream_ttfb_ms` p95 | < 800 ms |
| `stream_segment_ttfb_ms` p95 | < 500 ms |
| `stream_segment_errors` | < 2% |

Script tự gọi endpoint refresh phiên khi gặp `403` do token hết hạn (tối đa một
lần mỗi vòng lặp) và đếm vào metric `stream_token_refreshes`.

## 5. Đọc kết quả

- `stream_ttfb_ms`: thời gian tới byte đầu của manifest/MP4.
- `stream_segment_ttfb_ms`: thời gian tới byte đầu của segment.
- `stream_segment_errors`: tỷ lệ segment không trả `200/206`.
- `stream_token_refreshes`: số lần phải gia hạn token — tăng cao nghĩa là TTL
  hoặc vòng refresh 70% chưa ổn định.

Khi chạy trên môi trường production, hãy dùng một episode/nguồn riêng và bật
theo dõi `Retry-After`, circuit breaker và tỷ lệ `503` để phát hiện nguồn CDN yếu.
