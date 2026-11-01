// =============================================================================
// CinemaFlow - k6 load test cho luồng phát VOD/HLS (P1-07)
//
// Kịch bản:
//   1. setup() đăng nhập một hoặc nhiều tài khoản và ký một phiên phát mỗi tài khoản.
//   2. Mỗi VU tải manifest (hoặc Range đầu tiên với MP4), sau đó tải vài segment.
//   3. Nếu URL hết hạn (403), VU gọi endpoint refresh phiên đúng một lần rồi thử lại.
//
// Chạy nhanh (1 tài khoản, dùng cho kiểm thử edge/CDN):
//   k6 run -e BASE_URL=http://localhost:5085 ^
//           -e EMAIL=demo@cinemaflow.vn -e PASSWORD=Password123! ^
//           -e EPISODE_ID=1 -e POSITION=1 tests/load/stream-playback.js
//
// Chạy 1.000 VU với nhiều tài khoản (khuyến nghị cho P1-07):
//   k6 run -e BASE_URL=https://cinemaflow.vn ^
//           -e ACCOUNTS="load01@test.vn:Password123!,load02@test.vn:Password123!" ^
//           -e EPISODE_ID=1 -e POSITION=1 -e VUS=1000 tests/load/stream-playback.js
//
// Lưu ý: mỗi tài khoản bị giới hạn số phiên đồng thời theo gói (Free=1, VIP=2,
// Premium=4). Với 1.000 VU cần pool tài khoản đủ lớn hoặc dùng chế độ chia sẻ
// một phiên (các VU cùng IP load generator) để đo riêng năng lực segment/edge.
// =============================================================================

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Trend, Rate, Counter } from 'k6/metrics';

const BASE_URL = (__ENV.BASE_URL || 'http://localhost:5085').replace(/\/+$/, '');
const EPISODE_ID = Number(__ENV.EPISODE_ID || '1');
const POSITION = Number(__ENV.POSITION || '1');
const TARGET_VUS = Number(__ENV.VUS || '1000');
const SEGMENTS_PER_ITERATION = Number(__ENV.SEGMENTS || '3');
const ACCOUNTS_RAW = (__ENV.ACCOUNTS || '').trim();
const DEFAULT_EMAIL = __ENV.EMAIL || 'demo@cinemaflow.vn';
const DEFAULT_PASSWORD = __ENV.PASSWORD || 'Password123!';

const streamTtfb = new Trend('stream_ttfb_ms', true);
const segmentTtfb = new Trend('stream_segment_ttfb_ms', true);
const segmentErrors = new Rate('stream_segment_errors');
const refreshCount = new Counter('stream_token_refreshes');

export const options = {
  scenarios: {
    playback: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: __ENV.RAMP_UP || '1m', target: Math.max(1, Math.ceil(TARGET_VUS * 0.25)) },
        { duration: __ENV.RAMP_TO_PEAK || '2m', target: TARGET_VUS },
        { duration: __ENV.HOLD || '5m', target: TARGET_VUS },
        { duration: __ENV.RAMP_DOWN || '1m', target: 0 },
      ],
      gracefulRampDown: '30s',
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<1200'],
    stream_ttfb_ms: ['p(95)<800'],
    stream_segment_ttfb_ms: ['p(95)<500'],
    stream_segment_errors: ['rate<0.02'],
  },
};

function parseAccounts() {
  if (!ACCOUNTS_RAW) return [{ email: DEFAULT_EMAIL, password: DEFAULT_PASSWORD }];
  return ACCOUNTS_RAW.split(',')
    .map((entry) => entry.trim())
    .filter(Boolean)
    .map((entry) => {
      const separator = entry.indexOf(':');
      return separator > 0
        ? { email: entry.slice(0, separator), password: entry.slice(separator + 1) }
        : { email: entry, password: DEFAULT_PASSWORD };
    });
}

function login(email, password) {
  const response = http.post(
    `${BASE_URL}/api/auth/login`,
    JSON.stringify({ email, password }),
    { headers: { 'Content-Type': 'application/json' }, tags: { name: 'login' } },
  );

  if (response.status !== 200) {
    throw new Error(`Login thất bại cho ${email}: HTTP ${response.status} ${response.body}`);
  }

  const token = response.json('data.accessToken');
  if (!token) {
    throw new Error(`Login không trả accessToken cho ${email}`);
  }

  return token;
}

function signStream(token) {
  const response = http.post(
    `${BASE_URL}/api/stream/sign`,
    JSON.stringify({ episodeId: EPISODE_ID, position: POSITION }),
    {
      headers: {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${token}`,
      },
      tags: { name: 'stream-sign' },
    },
  );

  if (response.status !== 200) {
    throw new Error(`Không ký được luồng phát: HTTP ${response.status} ${response.body}`);
  }

  const data = response.json('data');
  if (!data || !data.streamUrl) {
    throw new Error('Phản hồi ký luồng không có streamUrl');
  }

  return {
    url: data.streamUrl,
    sessionId: data.sessionId || null,
    expiresAt: data.expiresAt,
  };
}

function refreshStream(token, sessionId) {
  if (!sessionId) return null;

  refreshCount.add(1);
  const response = http.post(
    `${BASE_URL}/api/stream/sessions/${encodeURIComponent(sessionId)}/refresh`,
    JSON.stringify({ episodeId: EPISODE_ID, position: POSITION }),
    {
      headers: {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${token}`,
      },
      tags: { name: 'stream-refresh' },
    },
  );

  if (response.status !== 200) return null;
  const data = response.json('data');
  return data && data.streamUrl ? { url: data.streamUrl, sessionId: data.sessionId || sessionId } : null;
}

export function setup() {
  const accounts = parseAccounts();
  const streams = accounts.map((account) => {
    const token = login(account.email, account.password);
    return { token, ...signStream(token) };
  });

  return { streams };
}

function isManifest(response) {
  const contentType = (response.headers['Content-Type'] || response.headers['content-type'] || '').toLowerCase();
  return contentType.includes('mpegurl') || (response.body || '').trimStart().startsWith('#EXTM3U');
}

function resolveUrl(uri, baseUrl) {
  if (/^https?:\/\//i.test(uri)) return uri;
  if (uri.startsWith('/')) return `${BASE_URL}${uri}`;
  return new URL(uri, baseUrl).toString();
}

function manifestUris(body, manifestUrl) {
  const uris = [];
  const lines = (body || '').split(/\r?\n/);
  for (const rawLine of lines) {
    const line = rawLine.trim();
    if (!line) continue;

    if (line.startsWith('#')) {
      const match = line.match(/URI="([^"]+)"/i);
      if (match && !match[1].startsWith('data:')) {
        uris.push(resolveUrl(match[1], manifestUrl));
      }
      continue;
    }

    if (line.startsWith('data:') || line.startsWith('skd:') || line.startsWith('urn:')) continue;
    uris.push(resolveUrl(line, manifestUrl));
  }

  return uris;
}

function fetchSegment(url, index) {
  const response = http.get(url, {
    headers: { Range: 'bytes=0-262143' },
    tags: { name: 'stream-segment' },
  });

  segmentTtfb.add(response.timings.waiting);
  const ok = check(response, {
    [`segment ${index} trả 200/206`]: (res) => res.status === 200 || res.status === 206,
  });
  segmentErrors.add(!ok);
}

export default function (data) {
  const streams = data.streams;
  const stream = streams[(__VU - 1) % streams.length];
  let currentUrl = stream.url;
  let sessionId = stream.sessionId;

  let manifestResponse = http.get(currentUrl, {
    headers: { Range: 'bytes=0-65535' },
    tags: { name: 'stream-play' },
  });

  if (manifestResponse.status === 403) {
    const refreshed = refreshStream(stream.token, sessionId);
    if (refreshed) {
      currentUrl = refreshed.url;
      sessionId = refreshed.sessionId;
      manifestResponse = http.get(currentUrl, {
        headers: { Range: 'bytes=0-65535' },
        tags: { name: 'stream-play' },
      });
    }
  }

  streamTtfb.add(manifestResponse.timings.waiting);

  const playOk = check(manifestResponse, {
    'play trả 200/206': (res) => res.status === 200 || res.status === 206,
  });
  if (!playOk) {
    sleep(1);
    return;
  }

  if (!isManifest(manifestResponse)) {
    // VOD MP4: tải một phần dữ liệu để đo TTFB và băng thông.
    check(manifestResponse, {
      'mp4 có dữ liệu': (res) => (res.body || '').length > 0,
    });
    sleep(1 + Math.random());
    return;
  }

  const uris = manifestUris(manifestResponse.body, currentUrl);
  const segmentUris = uris.filter((uri) => !/\.m3u8(\?|$)/i.test(uri));
  const selected = segmentUris.slice(0, Math.max(1, SEGMENTS_PER_ITERATION));

  for (let index = 0; index < selected.length; index += 1) {
    fetchSegment(selected[index], index);
  }

  sleep(1 + Math.random() * 2);
}
