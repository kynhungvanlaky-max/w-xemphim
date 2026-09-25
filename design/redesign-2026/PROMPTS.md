# ImageGen prompt briefs

Mode: built-in ImageGen. Không dùng CLI/API fallback.
Đây là prompt set được chuẩn hóa để đọc và tái tạo direction, gồm shared brief và từng page/asset brief. Các prompt từng lượt có thêm ràng buộc kỹ thuật/copy tương ứng; không có seed cố định.

## Shared UI brief

Use case: `ui-mockup`. One separate readable horizontal desktop reference for Vietnamese CinemaFlow, implementation-ready real web UI screenshot, not a moodboard or multi-page collage. Cinema-grade deep dark editorial: Obsidian `#08090B`, Graphite `#121418`, Bone `#F2EFE8`, Crimson `#D31920` for action and active states, Amber `#D8A447` in cinematic photography/membership context. Refined grotesk UI with Vietnamese glyphs; compressed display movie title. Header single line under 76px. 48-64px gutters. Consistent 10px radius consumer, 8px admin. `DESIGN_VARIANCE: 7`, `MOTION_INTENSITY: 6`, `VISUAL_DENSITY: 5`. Clean hero, readable CTA, image-led gallery, fixed-aspect media. Avoid purple gradients, neon, glass overload, nested cards, circular spinners, fake statistics, excessive pills, tiny unreadable text, English decoration, watermarks and browser chrome. Motion implication: cinematic fade-through and subtle parallax.

## Selected screen briefs

| Output | Page-specific prompt brief |
|---|---|
| home.png | Full-bleed Vietnamese rice-field dusk rain key art, copy on left third, actor/landscape focal point right, title MƯA TRÊN CÁNH ĐỒNG maximum two lines, Xem ngay CTA, Đang thịnh hành portrait rail entering fold. |
| home-content.png | Dedicated lower-content screen, Tiếp tục xem with three large landscape stills and thin real viewing progress; asymmetric Điện ảnh Việt spotlight beside portrait posters; minimal footer. Not a cropped hero. |
| explore.png | KHÁM PHÁ title, generous search, precise genre/country/year/sort filter rail, asymmetric gallery with one wide featured film and portrait posters, readable captions outside images. |
| genres.png | Thể loại phim, one featured Chính kịch landscape tile, stacked Hành động and Tình cảm tiles, lower sci-fi/animation/horror row; labels outside images, sample counts explicitly marked. |
| random.png | Để điện ảnh chọn bạn. left; recommendation landscape right; clearly separate Chọn một bộ phim, Xem phim, Thử lựa chọn khác actions; no roulette wheel or dice. |
| movie-detail.png | Rice-field rain backdrop, portrait poster left, large title/metadata/actions, Xem phim/Xem trailer/save, tab rail, editorial synopsis/cast beside compact specs; no floating quote. |
| watch.png | Distraction-free player around 72% width with clear controls/crimson progress; episode drawer around 28%; active episode crimson edge; compact title and next-episode action below. |
| watchlist.png | Tủ phim của bạn, poster grid with captions and saved/remove UI, restrained sort/filter, continue-watching artwork at fold only if data supports it. |
| history.png | Lịch sử xem, personal library switch, large featured resume still then compact history rows with episode/progress/Xem tiếp; no invented analytics. |
| account-v2.png | Existing-capability-only account screen: identity, quick links settings/history/notifications, membership rights/expiry/renew, payment history, logout. No devices or sessions feature. |
| profile-settings.png | Account rail, Phát video quality dropdown/autoplay toggle, language and episode notification group, Lưu thay đổi and inline success. No new settings. |
| notifications.png | Three actual notification copy examples, unread crimson edge, time, Mark all read action, cinematic panorama right; no unrelated controls. |
| login-v2.png | Rain-field left 58%, form right 42%, Email hoặc Tên đăng nhập, password/forgot/error/login/register. Edit corrected wordmark and removed decorative slogan; compact existing demo account disclosure. |
| register-v2.png | Same auth grammar, four existing fields, password guidance, edit corrected Nhập lại mật khẩu, added exact existing consent checkbox, CTA Tạo tài khoản. |
| forgot-password.png | Same auth grammar, one email input, concise recovery guidance, Gửi hướng dẫn, back-login link, inline confirmation example. |
| reset-password.png | Same auth grammar, new password/confirmation fields, password guidance, inline mismatch example, primary password-save action, back-login link. |
| vip.png | Clean single-section hero, Xem trọn cảm xúc. Không bị ngắt quãng., warm cinema corridor figure right, Chọn gói VIP, flat separate four-benefit rail, next pricing heading at fold. |
| vip-plans.png | Dedicated pricing section: Một tháng 79.000đ, Ba tháng 199.000đ, Một năm 699.000đ; center selected with crimson; shared benefits once; compact checkout; no fake discount/free trial. |
| admin.png | Operations workspace: sidebar, compact KPI band, large movie-management table, readable thumbnail/title/status/rating/time/actions, narrow activity/content-health column; no marketing hero. |
| not-found.png | Calm 404 composition, recovery headline/guidance, home/explore actions, empty cinema photo, no cute illustration or fake terminal. |
| error.png | Calm contextual error, Có lỗi xảy ra, inline guidance and recovery actions, projector photo, no stack trace or random request ID. |

## Clean asset prompts

### cinemaflow-hero-rice-field.png

Use case: photorealistic-natural. Asset type: CinemaFlow homepage hero background, no UI. Ultra-wide cinematic key art of a lone Vietnamese farmer in a dark rain cape and conical hat walking through flooded rice fields during a violent dusk rainstorm; distant limestone mountains, one humble warm-lit farmhouse, reflective water, emotionally restrained prestige-drama tone. Photorealistic Vietnamese cinema still, natural materials, anamorphic composition, fine film grain. 2.2:1 wide landscape; subject on right third, generous dark negative space on left for website headline, horizon in upper third, layered depth. Stormy charcoal sky broken by amber sunset, practical lights, wet reflections. Obsidian, wet earth, muted rice green, amber highlights, no saturated cyan. No text, logo, interface, watermark, border or recognizable celebrity; culturally accurate setting.

### cinemaflow-watch-rain-night.png

Use case: photorealistic-natural. Asset type: watch-player episode still, no UI. Wide cinematic night scene in a Vietnamese rice field under heavy rain; lone farmer in conical hat carrying an old warm lantern, seen from behind, walking toward a small wooden home with amber windows. Photorealistic prestige film frame, realistic rain, wet fabric, subtle grain and natural light. 16:9 landscape, centered-low human figure, lantern in foreground, deep field perspective, safe player crop. Moonlit blue-black night balanced by amber lanterns, somber and intimate. No text, logo, interface, watermark, celebrity, purple or neon.

### cinemaflow-poster-rain-field.png

Use case: product-mockup. Asset type: original movie portrait poster artwork, no UI. Fictional Vietnamese prestige drama MƯA TRÊN CÁNH ĐỒNG; rain-soaked Vietnamese woman in profile in upper half, flooded rice fields and lone conical-hat silhouette below, amber horizon through charcoal clouds. Photorealistic premium key art, restrained editorial poster, analog grain. 2:3 portrait, strong silhouette and layered double-exposure feeling, breathing room for later title treatment but no rendered letters. Obsidian, stone, muted green, bone, amber. Absolutely no text, letters, logo, credits, watermark, border or famous actor likeness.

### cinemaflow-projector.png

Use case: photorealistic-natural. Asset type: clean original ambient background for explore header, account and contextual error. Wide 16:9 photo of an old film projector on right third in an authentic dark projection room, pale warm beam through subtle dust, tactile metal and film reels. Rest deep obsidian with broad negative space. Prestige editorial cinematography, fine grain, natural amber highlights, restrained exposure. No letters, slogans, numbers, signs, logos, interface, watermark, border, purple, neon or famous people.

### cinemaflow-vip-corridor.png

Use case: photorealistic-natural. Asset type: clean VIP hero photo. Wide 2.2:1 original film still of a lone adult Vietnamese figure in elegant dark clothing on right third in a premium vintage cinema corridor, amber wall lights, restrained velvet crimson accents, reflective dark floor, nostalgic modern film mood. Left half dark uncluttered negative space. Realistic cinematography, fine grain, believable architecture. No text, logo, signs, UI, watermark, celebrity likeness, purple or neon.

### cinemaflow-auth-rain.png

Use case: photorealistic-natural. Asset type: authentication background photograph, 4:5 portrait. Vietnamese farmer from behind in dark rain cape/conical hat walking through flooded rice fields during night rain, soft amber lantern, distant limestone mountains, one warm farmhouse, layered depth and reflective water. Upper half dark storm sky for brand overlay. Premium photorealistic cinema frame, muted green/obsidian/amber grade, grain. No letters, slogan, logo, credits, watermark, UI, border, famous face, purple or neon.

## Review notes

All selected outputs were visually inspected. ImageGen has introduced some decorative text/font/metadata variation despite constraints. `DESIGN_DIRECTION.md` lists canonical corrections required before production. Asset generation is standalone, not cropping old UI mockups. The clean asset pack matches the art direction but is not asserted to be a pixel-identical extraction of every in-mockup photograph. Runtime film imagery must remain accurate to catalog data.
