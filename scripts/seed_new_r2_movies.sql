SET search_path TO webxemphim, public;

-- Thêm các thể loại nếu chưa có
INSERT INTO genres ("Id", "Name", "Slug") VALUES
(16, 'Công Nghệ', 'cong-nghe'),
(17, 'Âm Nhạc', 'am-nhac')
ON CONFLICT ("Id") DO NOTHING;

-- Phim 41: Hướng Dẫn Cài Giao Diện Sạch Tivi Android
INSERT INTO movies (
    "Id", "Title", "OriginalTitle", "Slug", "Overview",
    "PosterUrl", "BackdropUrl", "TrailerUrl", "Rating", "VoteCount",
    "ReleaseYear", "DurationMinutes", "AgeRating", "Quality", "Type",
    "Country", "ViewCount", "IsTrending", "IsFeatured", "Director",
    "SearchText", "TotalEpisodes", "CurrentEpisode"
) VALUES (
    41,
    'Hướng Dẫn Cài Giao Diện Sạch Tivi Android',
    'Clean UI & Debloat Android TV - No Laptop AI',
    'huong-dan-cai-giao-dien-sach-tivi-android',
    'Video hướng dẫn thực tế cài giao diện sạch (Clean UI), xoá ứng dụng rác cho Android TV cực dễ dàng mà không cần dùng đến laptop hay AI. Tối ưu bộ nhớ, loại bỏ quảng cáo và giúp tivi hoạt động mượt mà.',
    '/images/movies/movie-1-poster.webp',
    '/images/movies/movie-1-backdrop.webp',
    'https://www.youtube.com/watch?v=Way9Dexny3w',
    9.8, 128500, 2026, 25, 'All', '1080p FHD', 1,
    'Việt Nam', 2500000, true, true, 'Kỳ Như',
    'Huong Dan Cai Giao Dien Sach Xoa App Rac Cho Tivi Android Cuc De Khong Can Laptop AI Ky Nhu',
    1, 1
) ON CONFLICT ("Id") DO UPDATE SET
    "Title" = EXCLUDED."Title",
    "OriginalTitle" = EXCLUDED."OriginalTitle",
    "Slug" = EXCLUDED."Slug",
    "Overview" = EXCLUDED."Overview",
    "IsTrending" = true,
    "IsFeatured" = true;

-- Phim 42: Xu Hướng Việc Làm Hà Nam 2026
INSERT INTO movies (
    "Id", "Title", "OriginalTitle", "Slug", "Overview",
    "PosterUrl", "BackdropUrl", "TrailerUrl", "Rating", "VoteCount",
    "ReleaseYear", "DurationMinutes", "AgeRating", "Quality", "Type",
    "Country", "ViewCount", "IsTrending", "IsFeatured", "Director",
    "SearchText", "TotalEpisodes", "CurrentEpisode"
) VALUES (
    42,
    'Xu Hướng Việc Làm Hà Nam 2026',
    'Trending TikTok Jobs Ha Nam 2026',
    'xu-huong-viec-lam-ha-nam-2026',
    'Tổng hợp clip xu hướng TikTok nổi bật năm 2026 về tìm kiếm việc làm, giới thiệu việc làm và các cơ hội nghề nghiệp tại Hà Nam.',
    '/images/movies/movie-2-poster.webp',
    '/images/movies/movie-2-backdrop.webp',
    'https://www.youtube.com/watch?v=Way9Dexny3w',
    9.5, 89000, 2026, 15, 'All', '1080p FHD', 1,
    'Việt Nam', 1800000, true, true, 'TikTok Creator',
    'Xu Huong Viec Lam Ha Nam TikTok 2026',
    1, 1
) ON CONFLICT ("Id") DO UPDATE SET
    "Title" = EXCLUDED."Title",
    "OriginalTitle" = EXCLUDED."OriginalTitle",
    "Slug" = EXCLUDED."Slug",
    "Overview" = EXCLUDED."Overview",
    "IsTrending" = true,
    "IsFeatured" = true;

-- Phim 43: Tuyển Tập Video MP4 Đặc Sắc
INSERT INTO movies (
    "Id", "Title", "OriginalTitle", "Slug", "Overview",
    "PosterUrl", "BackdropUrl", "TrailerUrl", "Rating", "VoteCount",
    "ReleaseYear", "DurationMinutes", "AgeRating", "Quality", "Type",
    "Country", "ViewCount", "IsTrending", "IsFeatured", "Director",
    "SearchText", "TotalEpisodes", "CurrentEpisode"
) VALUES (
    43,
    'Tuyển Tập Video MP4 Đặc Sắc',
    'Featured Video Short (Video.mp4)',
    'tuyen-tap-video-mp4-dac-sac',
    'Trích đoạn clip ngắn chất lượng cao từ kho lưu trữ đám mây cá nhân Cloudflare R2.',
    '/images/movies/movie-3-poster.webp',
    '/images/movies/movie-3-backdrop.webp',
    'https://www.youtube.com/watch?v=Way9Dexny3w',
    9.2, 64000, 2026, 5, 'All', '1080p FHD', 1,
    'Việt Nam', 1200000, true, false, 'CinemaFlow Studio',
    'Tuyen Tap Video MP4 Dac Sac Short Video R2',
    1, 1
) ON CONFLICT ("Id") DO UPDATE SET
    "Title" = EXCLUDED."Title",
    "OriginalTitle" = EXCLUDED."OriginalTitle",
    "Slug" = EXCLUDED."Slug",
    "Overview" = EXCLUDED."Overview";

-- Phim 44: Điện Ảnh Chọn Lọc (Play MP4)
INSERT INTO movies (
    "Id", "Title", "OriginalTitle", "Slug", "Overview",
    "PosterUrl", "BackdropUrl", "TrailerUrl", "Rating", "VoteCount",
    "ReleaseYear", "DurationMinutes", "AgeRating", "Quality", "Type",
    "Country", "ViewCount", "IsTrending", "IsFeatured", "Director",
    "SearchText", "TotalEpisodes", "CurrentEpisode"
) VALUES (
    44,
    'Điện Ảnh Chọn Lọc (Play MP4)',
    'Curated Cinema Feature (play.mp4)',
    'dien-anh-chon-loc-play-mp4',
    'Bản phim chiếu rạp chuẩn điện ảnh 72.8 MB được lưu trữ trực tiếp trên bucket cá nhân Cloudflare R2 tốc độ cao.',
    '/images/movies/movie-4-poster.webp',
    '/images/movies/movie-4-backdrop.webp',
    'https://www.youtube.com/watch?v=Way9Dexny3w',
    9.7, 195000, 2026, 45, '16+', '4K Ultra HD', 1,
    'Âu Mỹ', 3400000, true, true, 'CinemaFlow Master',
    'Dien Anh Chon Loc Play MP4 4K Cloudflare R2',
    1, 1
) ON CONFLICT ("Id") DO UPDATE SET
    "Title" = EXCLUDED."Title",
    "OriginalTitle" = EXCLUDED."OriginalTitle",
    "Slug" = EXCLUDED."Slug",
    "Overview" = EXCLUDED."Overview",
    "IsTrending" = true,
    "IsFeatured" = true;

-- Phim 45: Meyo - Half Asleep (Lyrics Official)
INSERT INTO movies (
    "Id", "Title", "OriginalTitle", "Slug", "Overview",
    "PosterUrl", "BackdropUrl", "TrailerUrl", "Rating", "VoteCount",
    "ReleaseYear", "DurationMinutes", "AgeRating", "Quality", "Type",
    "Country", "ViewCount", "IsTrending", "IsFeatured", "Director",
    "SearchText", "TotalEpisodes", "CurrentEpisode"
) VALUES (
    45,
    'Meyo - Half Asleep (Lyrics Official)',
    'Meyo - Half Asleep (Lyrics)',
    'meyo-half-asleep-lyrics',
    'Tác phẩm âm nhạc Meyo - Half Asleep với giai điệu sâu lắng và lời bài hát đầy cảm xúc.',
    '/images/movies/movie-5-poster.webp',
    '/images/movies/movie-5-backdrop.webp',
    'https://www.youtube.com/watch?v=Way9Dexny3w',
    9.4, 48000, 2026, 4, 'All', 'Lossless Audio', 1,
    'Quốc Tế', 890000, false, false, 'Meyo',
    'Meyo Half Asleep Lyrics Audio Music',
    1, 1
) ON CONFLICT ("Id") DO UPDATE SET
    "Title" = EXCLUDED."Title",
    "OriginalTitle" = EXCLUDED."OriginalTitle",
    "Slug" = EXCLUDED."Slug",
    "Overview" = EXCLUDED."Overview";

-- Genres cho các phim mới
INSERT INTO movie_genres ("MovieId", "GenreId") VALUES
(41, 16), (41, 2),
(42, 1), (42, 4),
(43, 1), (43, 5),
(44, 1), (44, 2), (44, 5),
(45, 17)
ON CONFLICT DO NOTHING;

-- Thêm Episodes cho 5 phim mới
INSERT INTO episodes ("Id", "MovieId", "EpisodeNumber", "Title", "DurationMinutes", "ThumbnailUrl", "Overview") VALUES
(4101, 41, 1, 'Bản Hướng Dẫn Chi Tiết (185 MB)', 25, '/images/movies/movie-1-backdrop.webp', 'Hướng dẫn từng bước cài đặt Clean UI và xoá app rác Android TV.'),
(4201, 42, 1, 'Video Xu Hướng TikTok (12.6 MB)', 15, '/images/movies/movie-2-backdrop.webp', 'Video clip ngắn xu hướng tuyển dụng việc làm Hà Nam 2026.'),
(4301, 43, 1, 'Clip Ngắn Đặc Sắc (3.9 MB)', 5, '/images/movies/movie-3-backdrop.webp', 'Clip video ngắn tuyển chọn chất lượng cao.'),
(4401, 44, 1, 'Bản Chiếu Đầy Đủ (72.8 MB)', 45, '/images/movies/movie-4-backdrop.webp', 'Bản phim chuẩn điện ảnh lưu trữ trên Cloudflare R2.'),
(4501, 45, 1, 'Bản Thu Gốc Meyo (4.2 MB)', 4, '/images/movies/movie-5-backdrop.webp', 'Bản thu âm thanh chất lượng cao kèm lời bài hát.')
ON CONFLICT ("Id") DO UPDATE SET
    "Title" = EXCLUDED."Title",
    "DurationMinutes" = EXCLUDED."DurationMinutes",
    "Overview" = EXCLUDED."Overview";

-- Thêm Stream Sources cho Episode 4101 (Hướng dẫn TV Android)
DELETE FROM stream_sources WHERE "EpisodeId" = 4101;
INSERT INTO stream_sources ("EpisodeId", "Position", "ServerName", "ServerType", "Quality", "StreamUrl") VALUES
(4101, 1, 'VIP 1 (R2 Tivi Android - 185MB)', 1, '1080p FHD', 'r2://webxemphim-movies/Hướng Dẫn Cài Giao Diện Sạch, Xoá App Rác Cho Tivi Android Cực Dễ - Không Cần Laptop, AI!.mp4'),
(4101, 2, 'VIP 1 (R2 Tivi Android - 185MB)', 1, '720p HD', 'r2://webxemphim-movies/Hướng Dẫn Cài Giao Diện Sạch, Xoá App Rác Cho Tivi Android Cực Dễ - Không Cần Laptop, AI!.mp4'),
(4101, 5, 'VIP 2 (R2 Backup - Video.mp4)', 1, '1080p FHD', 'r2://webxemphim-movies/Video.mp4'),
(4101, 8, 'Hydra CDN (R2 Tivi Android)', 2, '1080p FHD', 'r2://webxemphim-movies/Hướng Dẫn Cài Giao Diện Sạch, Xoá App Rác Cho Tivi Android Cực Dễ - Không Cần Laptop, AI!.mp4'),
(4101, 9, 'Hydra CDN (R2 Tivi Android)', 2, '720p HD', 'r2://webxemphim-movies/Hướng Dẫn Cài Giao Diện Sạch, Xoá App Rác Cho Tivi Android Cực Dễ - Không Cần Laptop, AI!.mp4'),
(4101, 11, 'Direct Backup (R2 Tivi Android)', 3, '720p HD', 'r2://webxemphim-movies/Hướng Dẫn Cài Giao Diện Sạch, Xoá App Rác Cho Tivi Android Cực Dễ - Không Cần Laptop, AI!.mp4');

-- Thêm Stream Sources cho Episode 4201 (Xu Hướng TikTok Hà Nam)
DELETE FROM stream_sources WHERE "EpisodeId" = 4201;
INSERT INTO stream_sources ("EpisodeId", "Position", "ServerName", "ServerType", "Quality", "StreamUrl") VALUES
(4201, 1, 'VIP 1 (R2 TikTok - 12.6MB)', 1, '1080p FHD', 'r2://webxemphim-movies/#xuhuong #xuhuongtiktok #xuhuongtiktok2026 #vieclam #vieclamhanam .mp4'),
(4201, 5, 'VIP 2 (R2 Backup - Video.mp4)', 1, '1080p FHD', 'r2://webxemphim-movies/Video.mp4'),
(4201, 8, 'Hydra CDN (R2 TikTok)', 2, '1080p FHD', 'r2://webxemphim-movies/#xuhuong #xuhuongtiktok #xuhuongtiktok2026 #vieclam #vieclamhanam .mp4'),
(4201, 11, 'Direct Backup (R2 TikTok)', 3, '720p HD', 'r2://webxemphim-movies/#xuhuong #xuhuongtiktok #xuhuongtiktok2026 #vieclam #vieclamhanam .mp4');

-- Thêm Stream Sources cho Episode 4301 (Video.mp4)
DELETE FROM stream_sources WHERE "EpisodeId" = 4301;
INSERT INTO stream_sources ("EpisodeId", "Position", "ServerName", "ServerType", "Quality", "StreamUrl") VALUES
(4301, 1, 'VIP 1 (R2 Video.mp4 - 3.9MB)', 1, '1080p FHD', 'r2://webxemphim-movies/Video.mp4'),
(4301, 5, 'VIP 2 (R2 Video.mp4 - 3.9MB)', 1, '720p HD', 'r2://webxemphim-movies/Video.mp4'),
(4301, 8, 'Hydra CDN (R2 Video.mp4)', 2, '1080p FHD', 'r2://webxemphim-movies/Video.mp4'),
(4301, 11, 'Direct Backup (R2 Video.mp4)', 3, '720p HD', 'r2://webxemphim-movies/Video.mp4');

-- Thêm Stream Sources cho Episode 4401 (play.mp4)
DELETE FROM stream_sources WHERE "EpisodeId" = 4401;
INSERT INTO stream_sources ("EpisodeId", "Position", "ServerName", "ServerType", "Quality", "StreamUrl") VALUES
(4401, 1, 'VIP 1 (R2 play.mp4 - 72.8MB)', 1, '4K Ultra HD', 'r2://webxemphim-movies/play.mp4'),
(4401, 2, 'VIP 1 (R2 play.mp4 - 72.8MB)', 1, '1080p FHD', 'r2://webxemphim-movies/play.mp4'),
(4401, 5, 'VIP 2 (R2 Backup - Video.mp4)', 1, '1080p FHD', 'r2://webxemphim-movies/Video.mp4'),
(4401, 8, 'Hydra CDN (R2 play.mp4)', 2, '1080p FHD', 'r2://webxemphim-movies/play.mp4'),
(4401, 9, 'Hydra CDN (R2 play.mp4)', 2, '720p HD', 'r2://webxemphim-movies/play.mp4'),
(4401, 11, 'Direct Backup (R2 play.mp4)', 3, '720p HD', 'r2://webxemphim-movies/play.mp4');

-- Thêm Stream Sources cho Episode 4501 (Meyo - Half Asleep)
DELETE FROM stream_sources WHERE "EpisodeId" = 4501;
INSERT INTO stream_sources ("EpisodeId", "Position", "ServerName", "ServerType", "Quality", "StreamUrl") VALUES
(4501, 1, 'VIP 1 (R2 Meyo MP3 - 4.2MB)', 1, 'Lossless Audio', 'r2://webxemphim-movies/Meyo - Half Asleep (Lyrics).mp3'),
(4501, 8, 'Hydra CDN (R2 Meyo MP3)', 2, 'Lossless Audio', 'r2://webxemphim-movies/Meyo - Half Asleep (Lyrics).mp3'),
(4501, 11, 'Direct Backup (R2 Meyo MP3)', 3, '320kbps Audio', 'r2://webxemphim-movies/Meyo - Half Asleep (Lyrics).mp3');

