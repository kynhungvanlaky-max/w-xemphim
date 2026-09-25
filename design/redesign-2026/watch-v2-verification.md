# Watch v2 verification — 2026-09-19

Implemented against the approved desktop, mobile and details references in this directory. Runtime content, episode counts, sources and reviews come from the API rather than the mockup's sample movie.

## Automated checks

- `dotnet build WebXemPhim.slnx`: succeeded, 0 warnings, 0 errors.
- `dotnet test --no-build`: 335 passed, 0 failed, 0 skipped.
- `node --check src/Client/WebXemPhim.Client/wwwroot/js/cinema-player.js`: passed.
- Added coverage for ordered episode navigation, query selection, source/quality selection, search, auto-next boundaries, strict movie fetching and authorized playback-issue persistence.

## Browser checks

- Desktop 1440px and mobile 390px/320px: no horizontal overflow; no broken loaded images.
- Episode navigation updates the query, title and selection; first/last boundaries disable the appropriate button.
- Search has a real empty state and clear action.
- Switching the actual Backup Stream source updates the media URL and quality to 720p HD.
- Auto-next switch can be turned off; end-of-video sequencing is unit tested.
- Share reports successful clipboard copy and provides a manual fallback on failure.
- Lights-off leaves the player above the curtain; lights-on restores the page.
- Information/discussion anchors stay on the current watch route.
- Guest actions redirect to login with the selected episode as return URL; the demo account returns to that route and shows its authenticated VIP status.
- Watchlist toggle was tested and its test addition removed afterward.
- Report dialog validates empty input, closes with Escape/close button, traps focus and returns focus to its trigger. Persistence is covered with an isolated integration-test database; no artificial live report was posted.
- Guest and authenticated discussion composers were inspected; no artificial live review was posted.

## Media limitation

Existing seeded Google sample-video URLs return HTTP 403 in this environment. The UI labels these as demo sources and shows a retryable playback error. Actual decoding, seeking, fullscreen/PiP during playback and natural end-to-next autoplay cannot be confirmed end-to-end until an accessible video source is supplied. No unrelated clip is silently substituted.
