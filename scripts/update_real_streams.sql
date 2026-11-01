-- Cập nhật tất cả các luồng phát demo sang luồng phim thật
UPDATE stream_sources SET StreamUrl = 'local://tears_of_steel_720p.mp4' WHERE Position = 1;
UPDATE stream_sources SET StreamUrl = 'local://sintel_full.mp4' WHERE Position = 2;
UPDATE stream_sources SET StreamUrl = 'local://big_buck_bunny_720p.mp4' WHERE Position = 3;
UPDATE stream_sources SET StreamUrl = 'https://s1.phim1280.tv/20231021/aV8FmFTN/index.m3u8' WHERE Position = 4;
UPDATE stream_sources SET StreamUrl = 'https://s3.phim1280.tv/20240407/nwypt43V/index.m3u8' WHERE Position = 5;
UPDATE stream_sources SET StreamUrl = 'https://s2.phim1280.tv/20230920/MjQp8ZWM/index.m3u8' WHERE Position = 6;
UPDATE stream_sources SET StreamUrl = 'https://s4.phim1280.tv/20250302/eUGu2byt/index.m3u8' WHERE Position = 7;
UPDATE stream_sources SET StreamUrl = 'https://s5.phim1280.tv/20250511/jOXh1s8R/index.m3u8' WHERE Position = 8;
UPDATE stream_sources SET StreamUrl = 'https://archive.org/download/BigBuckBunny_124/Content/big_buck_bunny_720p_surround.mp4' WHERE Position = 9;
UPDATE stream_sources SET StreamUrl = 'https://archive.org/download/Tears-of-Steel/tears_of_steel_720p.mp4' WHERE Position = 10;
UPDATE stream_sources SET StreamUrl = 'https://archive.org/download/Sintel/sintel-2048-stereo.mp4' WHERE Position = 11;
UPDATE stream_sources SET StreamUrl = 'local://tears_of_steel_720p.mp4' WHERE Position = 12;

-- Phim 1: Dune: Part Two / Part Three (Episode 101)
UPDATE stream_sources SET StreamUrl = 'local://tears_of_steel_720p.mp4' WHERE EpisodeId = 101 AND Position = 1;
UPDATE stream_sources SET StreamUrl = 'local://sintel_full.mp4' WHERE EpisodeId = 101 AND Position = 2;
UPDATE stream_sources SET StreamUrl = 'https://s3.phim1280.tv/20240407/nwypt43V/index.m3u8' WHERE EpisodeId = 101 AND Position IN (3, 4, 5, 6, 7, 8, 9);
UPDATE stream_sources SET StreamUrl = 'local://tears_of_steel_720p.mp4' WHERE EpisodeId = 101 AND Position IN (10, 11, 12);

-- Phim 2: Oppenheimer (Episode 201)
UPDATE stream_sources SET StreamUrl = 'local://sintel_full.mp4' WHERE EpisodeId = 201 AND Position IN (1, 2);
UPDATE stream_sources SET StreamUrl = 'https://s2.phim1280.tv/20230920/MjQp8ZWM/index.m3u8' WHERE EpisodeId = 201 AND Position IN (3, 4, 5, 6, 7, 8, 9);
UPDATE stream_sources SET StreamUrl = 'local://sintel_full.mp4' WHERE EpisodeId = 201 AND Position IN (10, 11, 12);

-- Phim 4: The Dark Knight (Episode 401)
UPDATE stream_sources SET StreamUrl = 'local://big_buck_bunny_720p.mp4' WHERE EpisodeId = 401 AND Position IN (1, 2);
UPDATE stream_sources SET StreamUrl = 'https://s1.phim1280.tv/20231021/aV8FmFTN/index.m3u8' WHERE EpisodeId = 401 AND Position IN (3, 4, 5, 6, 7, 8, 9);
UPDATE stream_sources SET StreamUrl = 'local://big_buck_bunny_720p.mp4' WHERE EpisodeId = 401 AND Position IN (10, 11, 12);

-- Phim 7: Interstellar (Episode 701)
UPDATE stream_sources SET StreamUrl = 'local://tears_of_steel_720p.mp4' WHERE EpisodeId = 701 AND Position = 1;
UPDATE stream_sources SET StreamUrl = 'https://s4.phim1280.tv/20250302/eUGu2byt/index.m3u8' WHERE EpisodeId = 701 AND Position IN (2, 3, 4, 5, 6, 7, 8, 9);
UPDATE stream_sources SET StreamUrl = 'local://tears_of_steel_720p.mp4' WHERE EpisodeId = 701 AND Position IN (10, 11, 12);

-- Phim 23: Top Gun: Maverick (Episode 2301)
UPDATE stream_sources SET StreamUrl = 'https://s5.phim1280.tv/20250511/jOXh1s8R/index.m3u8' WHERE EpisodeId = 2301 AND Position IN (1, 2, 3, 4, 5, 6, 7, 8, 9);
UPDATE stream_sources SET StreamUrl = 'local://big_buck_bunny_720p.mp4' WHERE EpisodeId = 2301 AND Position IN (10, 11, 12);

-- Phim 3: Arcane Season 2 (Tập 1 -> 9)
UPDATE stream_sources SET StreamUrl = 'https://s6.kkphimplayer6.com/20251024/b2cUj0uD/index.m3u8' WHERE EpisodeId = 301;
UPDATE stream_sources SET StreamUrl = 'https://s6.kkphimplayer6.com/20251024/LOR0wlQe/index.m3u8' WHERE EpisodeId = 302;
UPDATE stream_sources SET StreamUrl = 'https://s6.kkphimplayer6.com/20251024/wztpqTLV/index.m3u8' WHERE EpisodeId = 303;
UPDATE stream_sources SET StreamUrl = 'https://s6.kkphimplayer6.com/20251024/YRk8GJJs/index.m3u8' WHERE EpisodeId = 304;
UPDATE stream_sources SET StreamUrl = 'https://s6.kkphimplayer6.com/20251024/Ae9AeApz/index.m3u8' WHERE EpisodeId = 305;
UPDATE stream_sources SET StreamUrl = 'https://s6.kkphimplayer6.com/20251024/vwusUZRn/index.m3u8' WHERE EpisodeId = 306;
UPDATE stream_sources SET StreamUrl = 'https://s6.kkphimplayer6.com/20251024/2rKmyeMi/index.m3u8' WHERE EpisodeId = 307;
UPDATE stream_sources SET StreamUrl = 'https://s6.kkphimplayer6.com/20251024/8UwWJiRV/index.m3u8' WHERE EpisodeId = 308;
UPDATE stream_sources SET StreamUrl = 'https://s6.kkphimplayer6.com/20251024/OawGCUJj/index.m3u8' WHERE EpisodeId = 309;
