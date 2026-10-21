using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using WebXemPhim.Server.Security;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;

namespace WebXemPhim.Server.Data;

public static class DatabaseSeeder
{
    public static List<MovieDetailDto> Movies => CatalogMovies;
    public static List<GenreDto> Genres => CatalogGenres;
    public static List<MovieDetailDto> CatalogMovies { get; } =
    [
        // ==========================================
        // 1. XỨ CÁT: PHẦN BA (DUNE 3) - Phim Lẻ
        // ==========================================
        new()
        {
            Id = 1,
            Title = "Xứ Cát: Phần Ba - Đấng Tiên Tri",
            OriginalTitle = "Dune: Part Three - The Prophet",
            Slug = "dune-part-three",
            Overview = "Paul Atreides đối mặt với cuộc thánh chiến vũ trụ tàn khốc, gánh vác sứ mệnh tiên tri và số phận của toàn bộ vũ trụ khi các gia tộc hoàng gia trỗi dậy phản kháng.",
            PosterUrl = "/images/movies/movie-1-poster.webp",
            BackdropUrl = "/images/movies/movie-1-backdrop.webp",
            TrailerUrl = "https://www.youtube.com/watch?v=Way9Dexny3w",
            Rating = 8.9,
            VoteCount = 184500,
            ReleaseYear = 2026,
            DurationMinutes = 168,
            AgeRating = "16+",
            Quality = "4K UHD HDR",
            Type = MovieType.Movie,
            Genres = ["Khoa Học Viễn Tưởng", "Hành Động", "Phiêu Lưu", "Kịch Tính"],
            Country = "Âu Mỹ",
            ViewCount = 1250000,
            IsTrending = true,
            IsFeatured = true,
            Director = "Denis Villeneuve",
            Cast = ["Timothée Chalamet", "Zendaya", "Florence Pugh", "Javier Bardem", "Anya Taylor-Joy"],
            TotalEpisodes = 1,
            CurrentEpisode = 1,
            Episodes =
            [
                new()
                {
                    Id = 101,
                    MovieId = 1,
                    EpisodeNumber = 1,
                    Title = "Bản Chiếu Rạp Đầy Đủ (Director's Cut)",
                    DurationMinutes = 168,
                    ThumbnailUrl = "/images/movies/movie-1-backdrop.webp",
                    Overview = "Toàn bộ diễn biến hoành tráng của phần phim kết thúc bộ ba thiên anh hùng ca Xứ Cát.",
                    Streams =
                    [
                        new() { ServerName = "VIP FastServer 1 (Hà Nội)", ServerType = StreamServerType.VipFastServer, Quality = "4K 60fps", StreamUrl = "https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-1080p.mp4" },
                        new() { ServerName = "Hydra CDN 2 (Sài Gòn)", ServerType = StreamServerType.HydraCdn, Quality = "1080p FHD", StreamUrl = "https://media.w3.org/2010/05/sintel/trailer.mp4" },
                        new() { ServerName = "Backup Stream", ServerType = StreamServerType.BackupStream, Quality = "720p HD", StreamUrl = "https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-720p.mp4" }
                    ]
                }
            ]
        },

        // ==========================================
        // 2. OPPENHEIMER - Phim Lẻ
        // ==========================================
        new()
        {
            Id = 2,
            Title = "Oppenheimer: Kẻ Hủy Diệt Thế Giới",
            OriginalTitle = "Oppenheimer",
            Slug = "oppenheimer",
            Overview = "Câu chuyện về nhà vật lý lý thuyết J. Robert Oppenheimer, người lãnh đạo Dự án Manhattan phát triển vũ khí hạt nhân đầu tiên trong lịch sử nhân loại.",
            PosterUrl = "/images/movies/movie-2-poster.webp",
            BackdropUrl = "/images/movies/movie-2-backdrop.webp",
            TrailerUrl = "https://www.youtube.com/watch?v=uYPbbksJxIg",
            Rating = 8.9,
            VoteCount = 312000,
            ReleaseYear = 2023,
            DurationMinutes = 180,
            AgeRating = "18+",
            Quality = "4K IMAX",
            Type = MovieType.Movie,
            Genres = ["Lịch Sử", "Kịch Tính", "Tâm Lý"],
            Country = "Âu Mỹ",
            ViewCount = 2840000,
            IsTrending = true,
            IsFeatured = false,
            Director = "Christopher Nolan",
            Cast = ["Cillian Murphy", "Emily Blunt", "Matt Damon", "Robert Downey Jr."],
            TotalEpisodes = 1,
            CurrentEpisode = 1,
            Episodes =
            [
                new()
                {
                    Id = 201,
                    MovieId = 2,
                    EpisodeNumber = 1,
                    Title = "Bản Chiếu IMAX",
                    DurationMinutes = 180,
                    ThumbnailUrl = "/images/movies/movie-2-backdrop.webp",
                    Overview = "Kiệt tác điện ảnh đạt 7 giải Oscar của đạo diễn Christopher Nolan.",
                    Streams =
                    [
                        new() { ServerName = "VIP FastServer 1", ServerType = StreamServerType.VipFastServer, Quality = "4K HDR", StreamUrl = "https://vjs.zencdn.net/v/oceans.mp4" }
                    ]
                }
            ]
        },

        // ==========================================
        // 3. ARCANE MÙA 2 - Anime / Hoạt hình
        // ==========================================
        new()
        {
            Id = 3,
            Title = "Arcane: Liên Minh Huyền Thoại (Mùa 2)",
            OriginalTitle = "Arcane: League of Legends Season 2",
            Slug = "arcane-season-2",
            Overview = "Căng thẳng leo thang đỉnh điểm giữa thành phố tiến bộ Piltover và khu ổ chuột Zaun, đẩy hai chị em Vi và Jinx vào cuộc chiến sinh tử không lối thoát.",
            PosterUrl = "/images/movies/movie-3-poster.webp",
            BackdropUrl = "/images/movies/movie-3-backdrop.webp",
            TrailerUrl = "https://www.youtube.com/watch?v=ysqiEC6bLUI",
            Rating = 9.0,
            VoteCount = 289000,
            ReleaseYear = 2024,
            DurationMinutes = 45,
            AgeRating = "16+",
            Quality = "4K Dolby Vision",
            Type = MovieType.Anime,
            Genres = ["Hoạt Hình", "Hành Động", "Khoa Học Viễn Tưởng", "Kỳ Ảo"],
            Country = "Âu Mỹ",
            ViewCount = 3980000,
            IsTrending = true,
            IsFeatured = false,
            Director = "Christian Linke, Alex Yee",
            Cast = ["Hailee Steinfeld", "Ella Purnell", "Katie Leung"],
            TotalEpisodes = 9,
            CurrentEpisode = 9,
            Episodes =
            [
                new() { Id = 301, MovieId = 3, EpisodeNumber = 1, Title = "Tập 1: Mảnh Vỡ Quá Khứ", DurationMinutes = 44, ThumbnailUrl = "/images/movies/movie-3-backdrop.webp", Overview = "Khởi đầu bi kịch mới tại Piltover sau vụ tấn công vào hội đồng.", Streams = [new() { ServerName = "VIP FastServer 1", Quality = "1080p 60fps", StreamUrl = "https://www.w3schools.com/html/mov_bbb.mp4" }] },
                new() { Id = 302, MovieId = 3, EpisodeNumber = 2, Title = "Tập 2: Báo Động Đỏ", DurationMinutes = 46, ThumbnailUrl = "/images/movies/movie-3-backdrop.webp", Overview = "Vi gia nhập cảnh vệ Piltover để truy lùng Jinx.", Streams = [new() { ServerName = "VIP FastServer 1", Quality = "1080p 60fps", StreamUrl = "https://vjs.zencdn.net/v/oceans.mp4" }] },
                new() { Id = 303, MovieId = 3, EpisodeNumber = 3, Title = "Tập 3: Mắt Bão Zaun", DurationMinutes = 48, ThumbnailUrl = "/images/movies/movie-3-backdrop.webp", Overview = "Cuộc đụng độ nghẹt thở giữa các phe phái bóng tối.", Streams = [new() { ServerName = "VIP FastServer 1", Quality = "1080p 60fps", StreamUrl = "https://media.w3.org/2010/05/sintel/trailer.mp4" }] }
            ]
        },

        // ==========================================
        // 4. KỴ SĨ BÓNG ĐÊM - Phim Lẻ
        // ==========================================
        new()
        {
            Id = 4,
            Title = "Kỵ Sĩ Bóng Đêm",
            OriginalTitle = "The Dark Knight",
            Slug = "the-dark-knight",
            Overview = "Khi hiểm họa mang tên Joker xuất hiện tàn phá Gotham, Batman phải trải qua bài kiểm tra lớn nhất về tâm lý và thể xác để bảo vệ công lý.",
            PosterUrl = "/images/movies/movie-4-poster.webp",
            BackdropUrl = "/images/movies/movie-4-backdrop.webp",
            TrailerUrl = "https://www.youtube.com/watch?v=EXeTwQWrcwY",
            Rating = 9.0,
            VoteCount = 420000,
            ReleaseYear = 2008,
            DurationMinutes = 152,
            AgeRating = "16+",
            Quality = "4K Remastered",
            Type = MovieType.Movie,
            Genres = ["Hành Động", "Tội Phạm", "Kịch Tính"],
            Country = "Âu Mỹ",
            ViewCount = 5200000,
            IsTrending = true,
            IsFeatured = false,
            Director = "Christopher Nolan",
            Cast = ["Christian Bale", "Heath Ledger", "Aaron Eckhart", "Gary Oldman"],
            TotalEpisodes = 1,
            CurrentEpisode = 1,
            Episodes =
            [
                new() { Id = 401, MovieId = 4, EpisodeNumber = 1, Title = "Bản Remaster 4K UHD", DurationMinutes = 152, ThumbnailUrl = "/images/movies/movie-4-backdrop.webp", Streams = [new() { ServerName = "VIP FastServer 1", Quality = "4K UHD", StreamUrl = "https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-1080p.mp4" }] }
            ]
        },

        // ==========================================
        // 5. SOLO LEVELING (MÙA 2) - Anime
        // ==========================================
        new()
        {
            Id = 5,
            Title = "Solo Leveling: Tôi Thăng Cấp Một Mình (Mùa 2)",
            OriginalTitle = "Solo Leveling: Arise from the Shadow",
            Slug = "solo-leveling-season-2",
            Overview = "Thợ săn yếu nhất thế giới Sung Jinwoo tiếp tục đánh thức sức mạnh Chúa Tể Bóng Tối, dẫn dắt đội quân bóng đêm càn quét các Hầm Ngục cấp S.",
            PosterUrl = "/images/movies/movie-5-poster.webp",
            BackdropUrl = "/images/movies/movie-5-backdrop.webp",
            TrailerUrl = "https://www.youtube.com/watch?v=byJ7pxxhaDY",
            Rating = 8.8,
            VoteCount = 195000,
            ReleaseYear = 2025,
            DurationMinutes = 24,
            AgeRating = "16+",
            Quality = "Full HD 1080p",
            Type = MovieType.Anime,
            Genres = ["Anime", "Hành Động", "Kỳ Ảo", "Phiêu Lưu"],
            Country = "Nhật Bản",
            ViewCount = 4120000,
            IsTrending = true,
            IsFeatured = false,
            Director = "Shunsuke Nakashige",
            Cast = ["Taito Ban", "Genta Nakamura", "Reina Ueda"],
            TotalEpisodes = 12,
            CurrentEpisode = 10,
            Episodes =
            [
                new() { Id = 501, MovieId = 5, EpisodeNumber = 1, Title = "Tập 1: Đội Quân Trỗi Dậy", DurationMinutes = 24, ThumbnailUrl = "/images/movies/movie-5-backdrop.webp", Streams = [new() { ServerName = "VIP FastServer 1", Quality = "1080p 60fps", StreamUrl = "https://vjs.zencdn.net/v/oceans.mp4" }] },
                new() { Id = 502, MovieId = 5, EpisodeNumber = 2, Title = "Tập 2: Lãnh Địa Quỷ Đỏ", DurationMinutes = 24, ThumbnailUrl = "/images/movies/movie-5-backdrop.webp", Streams = [new() { ServerName = "VIP FastServer 1", Quality = "1080p 60fps", StreamUrl = "https://vjs.zencdn.net/v/oceans.mp4" }] }
            ]
        },

        // ==========================================
        // 6. SHOGUN: TƯỚNG QUÂN - Phim Bộ (Series)
        // ==========================================
        new()
        {
            Id = 6,
            Title = "Shogun: Tướng Quân",
            OriginalTitle = "Shōgun",
            Slug = "shogun",
            Overview = "Tại Nhật Bản thời phong kiến năm 1600, Lãnh chúa Yoshii Toranaga chiến đấu bảo vệ tính mạng khi các kẻ thù trong Hội đồng Nhiếp chính hợp lực chống lại ông.",
            PosterUrl = "/images/movies/movie-6-poster.webp",
            BackdropUrl = "/images/movies/movie-6-backdrop.webp",
            TrailerUrl = "https://www.youtube.com/watch?v=yAN5uspO_hk",
            Rating = 8.8,
            VoteCount = 175000,
            ReleaseYear = 2024,
            DurationMinutes = 60,
            AgeRating = "18+",
            Quality = "4K UHD HDR",
            Type = MovieType.Series,
            Genres = ["Lịch Sử", "Kịch Tính", "Chiến Tranh"],
            Country = "Âu Mỹ",
            ViewCount = 2950000,
            IsTrending = true,
            IsFeatured = false,
            Director = "Rachel Kondo, Justin Marks",
            Cast = ["Hiroyuki Sanada", "Cosmo Jarvis", "Anna Sawai", "Tadanobu Asano"],
            TotalEpisodes = 10,
            CurrentEpisode = 10,
            Episodes =
            [
                new() { Id = 601, MovieId = 6, EpisodeNumber = 1, Title = "Tập 1: Kẻ Ngoại Quốc", DurationMinutes = 60, ThumbnailUrl = "/images/movies/movie-6-backdrop.webp", Streams = [new() { ServerName = "VIP FastServer 1", Quality = "4K UHD", StreamUrl = "https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-720p.mp4" }] },
                new() { Id = 602, MovieId = 6, EpisodeNumber = 2, Title = "Tập 2: Đầy Tớ Của Hai Chủ", DurationMinutes = 58, ThumbnailUrl = "/images/movies/movie-6-backdrop.webp", Streams = [new() { ServerName = "VIP FastServer 1", Quality = "4K UHD", StreamUrl = "https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-720p.mp4" }] }
            ]
        },

        // ==========================================
        // 7. HỐ ĐEN TỬ THẦN (INTERSTELLAR) - Phim Lẻ
        // ==========================================
        new()
        {
            Id = 7,
            Title = "Hố Đen Tử Thần",
            OriginalTitle = "Interstellar",
            Slug = "interstellar",
            Overview = "Khi Trái Đất bên bờ vực diệt vong, một nhóm nhà thám hiểm du hành qua lỗ sâu không gian để tìm kiếm ngôi nhà mới cho nhân loại.",
            PosterUrl = "/images/movies/movie-7-poster.webp",
            BackdropUrl = "/images/movies/movie-7-backdrop.webp",
            TrailerUrl = "https://www.youtube.com/watch?v=zSWdZVtXT7E",
            Rating = 8.7,
            VoteCount = 380000,
            ReleaseYear = 2014,
            DurationMinutes = 169,
            AgeRating = "13+",
            Quality = "4K Remastered",
            Type = MovieType.Movie,
            Genres = ["Khoa Học Viễn Tưởng", "Phiêu Lưu", "Kịch Tính"],
            Country = "Âu Mỹ",
            ViewCount = 6100000,
            IsTrending = false,
            IsFeatured = false,
            Director = "Christopher Nolan",
            Cast = ["Matthew McConaughey", "Anne Hathaway", "Jessica Chastain", "Michael Caine"],
            TotalEpisodes = 1,
            CurrentEpisode = 1,
            Episodes =
            [
                new() { Id = 701, MovieId = 7, EpisodeNumber = 1, Title = "Bản Chiếu Đầy Đủ", DurationMinutes = 169, ThumbnailUrl = "/images/movies/movie-7-backdrop.webp", Streams = [new() { ServerName = "VIP FastServer 1", Quality = "4K UHD", StreamUrl = "https://media.w3.org/2010/05/sintel/trailer.mp4" }] }
            ]
        },

        // ==========================================
        // 8. CẬU BÉ MẤT TÍCH (STRANGER THINGS 5) - Series
        // ==========================================
        new()
        {
            Id = 8,
            Title = "Cậu Bé Mất Tích (Stranger Things Mùa 5)",
            OriginalTitle = "Stranger Things Season 5",
            Slug = "stranger-things-season-5",
            Overview = "Mùa cuối cùng khép lại cuộc chiến chống lại Vecna và Thế Giới Ngược (Upside Down) đe dọa nuốt chửng thị trấn Hawkins và cả thế giới.",
            PosterUrl = "/images/movies/movie-8-poster.webp",
            BackdropUrl = "/images/movies/movie-8-backdrop.webp",
            TrailerUrl = "https://www.youtube.com/watch?v=b9EkMc79ZSU",
            Rating = 8.8,
            VoteCount = 310000,
            ReleaseYear = 2025,
            DurationMinutes = 65,
            AgeRating = "16+",
            Quality = "4K Dolby Vision",
            Type = MovieType.Series,
            Genres = ["Khoa Học Viễn Tưởng", "Kinh Dị", "Kỳ Ảo"],
            Country = "Âu Mỹ",
            ViewCount = 4800000,
            IsTrending = true,
            IsFeatured = false,
            Director = "The Duffer Brothers",
            Cast = ["Millie Bobby Brown", "Finn Wolfhard", "Winona Ryder", "David Harbour"],
            TotalEpisodes = 8,
            CurrentEpisode = 8,
            Episodes =
            [
                new() { Id = 801, MovieId = 8, EpisodeNumber = 1, Title = "Tập 1: Cuộc Thu Thập", DurationMinutes = 65, ThumbnailUrl = "/images/movies/movie-8-backdrop.webp", Streams = [new() { ServerName = "VIP FastServer 1", Quality = "4K UHD", StreamUrl = "https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-1080p.mp4" }] }
            ]
        },

        // ==========================================
        // 9. KẺ TRỘM GIẤC MƠ (INCEPTION) - Phim Lẻ
        // ==========================================
        new()
        {
            Id = 9,
            Title = "Kẻ Trộm Giấc Mơ",
            OriginalTitle = "Inception",
            Slug = "inception",
            Overview = "Dom Cobb là một đạo chích bậc thầy chuyên xâm nhập vào tiềm thức của mục tiêu trong khi họ đang mơ để đánh cắp những bí mật giá trị nhất.",
            PosterUrl = "/images/movies/movie-9-poster.webp",
            BackdropUrl = "/images/movies/movie-9-backdrop.webp",
            TrailerUrl = "https://www.youtube.com/watch?v=YoHD9XEInc0",
            Rating = 8.8,
            VoteCount = 390000,
            ReleaseYear = 2010,
            DurationMinutes = 148,
            AgeRating = "13+",
            Quality = "4K HDR",
            Type = MovieType.Movie,
            Genres = ["Khoa Học Viễn Tưởng", "Hành Động", "Kịch Tính"],
            Country = "Âu Mỹ",
            ViewCount = 4900000,
            IsTrending = false,
            IsFeatured = false,
            Director = "Christopher Nolan",
            Cast = ["Leonardo DiCaprio", "Joseph Gordon-Levitt", "Elliot Page", "Tom Hardy"],
            TotalEpisodes = 1,
            CurrentEpisode = 1,
            Episodes =
            [
                new() { Id = 901, MovieId = 9, EpisodeNumber = 1, Title = "Bản Chuẩn Điện Ảnh", DurationMinutes = 148, ThumbnailUrl = "/images/movies/movie-9-backdrop.webp", Streams = [new() { ServerName = "VIP FastServer 1", Quality = "4K UHD", StreamUrl = "https://vjs.zencdn.net/v/oceans.mp4" }] }
            ]
        },

        // ==========================================
        // 10. GIA TỘC RỒNG MÙA 2 - Series
        // ==========================================
        new()
        {
            Id = 10,
            Title = "Gia Tộc Rồng (House of the Dragon Mùa 2)",
            OriginalTitle = "House of the Dragon Season 2",
            Slug = "house-of-the-dragon-season-2",
            Overview = "Cuộc nội chiến 'Vũ Điệu Của Bầy Rồng' đẫm máu bùng nổ giữa hai phe Hắc Long (Rhaenyra) và Lục Long (Aegon II) để tranh giành Ngai Sắt.",
            PosterUrl = "/images/movies/movie-10-poster.webp",
            BackdropUrl = "/images/movies/movie-10-backdrop.webp",
            TrailerUrl = "https://www.youtube.com/watch?v=DotnJ7tTA34",
            Rating = 8.5,
            VoteCount = 210000,
            ReleaseYear = 2024,
            DurationMinutes = 62,
            AgeRating = "18+",
            Quality = "4K UHD",
            Type = MovieType.Series,
            Genres = ["Kỳ Ảo", "Hành Động", "Kịch Tính"],
            Country = "Âu Mỹ",
            ViewCount = 3450000,
            IsTrending = true,
            IsFeatured = false,
            Director = "Ryan Condal",
            Cast = ["Emma D'Arcy", "Matt Smith", "Olivia Cooke", "Rhys Ifans"],
            TotalEpisodes = 8,
            CurrentEpisode = 8,
            Episodes =
            [
                new() { Id = 1001, MovieId = 10, EpisodeNumber = 1, Title = "Tập 1: Nợ Máu Phải Trả Bằng Máu", DurationMinutes = 62, ThumbnailUrl = "/images/movies/movie-10-backdrop.webp", Streams = [new() { ServerName = "VIP FastServer 1", Quality = "4K UHD", StreamUrl = "https://media.w3.org/2010/05/sintel/trailer.mp4" }] }
            ]
        },

        // ==========================================
        // 11. KÝ SINH TRÙNG (PARASITE) - Phim Lẻ
        // ==========================================
        new()
        {
            Id = 11,
            Title = "Ký Sinh Trùng",
            OriginalTitle = "Parasite",
            Slug = "parasite",
            Overview = "Một gia đình nghèo khó lập kế hoạch tinh vi để từng người một thâm nhập và làm việc cho một gia đình thượng lưu giàu có tại Seoul.",
            PosterUrl = "/images/movies/movie-11-poster.webp",
            BackdropUrl = "/images/movies/movie-11-backdrop.webp",
            TrailerUrl = "https://www.youtube.com/watch?v=isOGD_7hNIY",
            Rating = 8.5,
            VoteCount = 280000,
            ReleaseYear = 2019,
            DurationMinutes = 132,
            AgeRating = "18+",
            Quality = "Full HD 1080p",
            Type = MovieType.Movie,
            Genres = ["Kịch Tính", "Tâm Lý", "Hài Hước"],
            Country = "Hàn Quốc",
            ViewCount = 3100000,
            IsTrending = false,
            IsFeatured = false,
            Director = "Bong Joon-ho",
            Cast = ["Song Kang-ho", "Lee Sun-kyun", "Cho Yeo-jeong", "Choi Woo-shik"],
            TotalEpisodes = 1,
            CurrentEpisode = 1,
            Episodes =
            [
                new() { Id = 1101, MovieId = 11, EpisodeNumber = 1, Title = "Bản Chiếu Rạp Đoạt Giải Oscar", DurationMinutes = 132, ThumbnailUrl = "/images/movies/movie-11-backdrop.webp", Streams = [new() { ServerName = "VIP FastServer 1", Quality = "1080p FHD", StreamUrl = "https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-1080p.mp4" }] }
            ]
        },

        // ==========================================
        // 12. CHÚ THUẬT HỒI CHIẾN - SHIBUYA - Anime
        // ==========================================
        new()
        {
            Id = 12,
            Title = "Chú Thuật Hồi Chiến: Biến Cố Shibuya",
            OriginalTitle = "Jujutsu Kaisen: Shibuya Incident",
            Slug = "jujutsu-kaisen-shibuya",
            Overview = "Vào đêm Halloween tại Shibuya, nhóm Chú Nguyền Sư do Kenjaku và Mahito cầm đầu thực hiện âm mưu phong ấn Chú Thuật Sư mạnh nhất: Gojo Satoru.",
            PosterUrl = "/images/movies/movie-12-poster.webp",
            BackdropUrl = "/images/movies/movie-12-backdrop.webp",
            TrailerUrl = "https://www.youtube.com/watch?v=9OhV3IWUsxE",
            Rating = 8.9,
            VoteCount = 220000,
            ReleaseYear = 2023,
            DurationMinutes = 24,
            AgeRating = "16+",
            Quality = "Full HD 1080p",
            Type = MovieType.Anime,
            Genres = ["Anime", "Hành Động", "Kỳ Ảo"],
            Country = "Nhật Bản",
            ViewCount = 3850000,
            IsTrending = true,
            IsFeatured = false,
            Director = "Sunghoo Park, Shota Goshozono",
            Cast = ["Junya Enoki", "Yuma Uchida", "Asami Seto", "Yuichi Nakamura"],
            TotalEpisodes = 23,
            CurrentEpisode = 23,
            Episodes =
            [
                new() { Id = 1201, MovieId = 12, EpisodeNumber = 1, Title = "Tập 1: Cánh Cổng Shibuya", DurationMinutes = 24, ThumbnailUrl = "/images/movies/movie-12-backdrop.webp", Streams = [new() { ServerName = "VIP FastServer 1", Quality = "1080p 60fps", StreamUrl = "https://vjs.zencdn.net/v/oceans.mp4" }] }
            ]
        },

        // ==========================================
        // 13. MAI (VIỆT NAM) - Phim Lẻ
        // ==========================================
        new()
        {
            Id = 13,
            Title = "Mai",
            OriginalTitle = "Mai",
            Slug = "mai",
            Overview = "Mai là một người phụ nữ ngoài ba mươi tuổi có quá khứ nhiều tổn thương, tìm kiếm tình yêu và sự chữa lành bên chàng trai trẻ Dương giữa định kiến xã hội.",
            PosterUrl = "/images/movies/movie-13-poster.webp",
            BackdropUrl = "/images/movies/movie-13-backdrop.webp",
            TrailerUrl = "https://www.youtube.com/watch?v=EX6clvId19s",
            Rating = 8.2,
            VoteCount = 145000,
            ReleaseYear = 2024,
            DurationMinutes = 131,
            AgeRating = "18+",
            Quality = "4K UHD",
            Type = MovieType.Movie,
            Genres = ["Tâm Lý", "Lãng Mạn", "Kịch Tính"],
            Country = "Việt Nam",
            ViewCount = 4520000,
            IsTrending = true,
            IsFeatured = false,
            Director = "Trấn Thành",
            Cast = ["Phương Anh Đào", "Tuấn Trần", "Hồng Đào", "Uyển Ân"],
            TotalEpisodes = 1,
            CurrentEpisode = 1,
            Episodes =
            [
                new() { Id = 1301, MovieId = 13, EpisodeNumber = 1, Title = "Bản Chiếu Rạp Chính Thức", DurationMinutes = 131, ThumbnailUrl = "/images/movies/movie-13-backdrop.webp", Streams = [new() { ServerName = "VIP FastServer 1", Quality = "4K UHD", StreamUrl = "https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-1080p.mp4" }] }
            ]
        },

        // ==========================================
        // 14. LẬT MẶT 7: MỘT ĐIỀU ƯỚC - Phim Lẻ
        // ==========================================
        new()
        {
            Id = 14,
            Title = "Lật Mặt 7: Một Điều Ước",
            OriginalTitle = "Face Off 7: One Wish",
            Slug = "lat-mat-7-mot-dieu-uoc",
            Overview = "Câu chuyện xúc động về bà Hai 73 tuổi cùng năm người con đã trưởng thành, khi mẹ già gặp nạn ai sẽ là người phụng dưỡng chăm sóc.",
            PosterUrl = "/images/movies/movie-14-poster.webp",
            BackdropUrl = "/images/movies/movie-14-backdrop.webp",
            TrailerUrl = "https://www.youtube.com/watch?v=QdtPQ0wV53M",
            Rating = 8.4,
            VoteCount = 162000,
            ReleaseYear = 2024,
            DurationMinutes = 138,
            AgeRating = "13+",
            Quality = "4K UHD",
            Type = MovieType.Movie,
            Genres = ["Gia Đình", "Kịch Tính", "Tâm Lý"],
            Country = "Việt Nam",
            ViewCount = 4850000,
            IsTrending = true,
            IsFeatured = false,
            Director = "Lý Hải",
            Cast = ["Thanh Hiền", "Trương Minh Cường", "Đinh Y Nhung", "Quách Ngọc Tuyên"],
            TotalEpisodes = 1,
            CurrentEpisode = 1,
            Episodes =
            [
                new() { Id = 1401, MovieId = 14, EpisodeNumber = 1, Title = "Bản Đầy Đủ", DurationMinutes = 138, ThumbnailUrl = "/images/movies/movie-14-backdrop.webp", Streams = [new() { ServerName = "VIP FastServer 1", Quality = "4K UHD", StreamUrl = "https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-720p.mp4" }] }
            ]
        },

        // ==========================================
        // 15. QUỶ CẨU (VIỆT NAM) - Phim Lẻ
        // ==========================================
        new()
        {
            Id = 15,
            Title = "Quỷ Cẩu",
            OriginalTitle = "Demon Dog",
            Slug = "quy-cau",
            Overview = "Lấy cảm hứng từ truyền thuyết linh dị dân gian Chó đội nón mê, kể về những tai ương rùng rợn giáng xuống một gia đình làm nghề mổ chó.",
            PosterUrl = "/images/movies/movie-15-poster.webp",
            BackdropUrl = "/images/movies/movie-15-backdrop.webp",
            TrailerUrl = "https://www.youtube.com/watch?v=Cswx649vXVw",
            Rating = 7.5,
            VoteCount = 82000,
            ReleaseYear = 2023,
            DurationMinutes = 105,
            AgeRating = "18+",
            Quality = "1080p FHD",
            Type = MovieType.Movie,
            Genres = ["Kinh Dị", "Tâm Lý"],
            Country = "Việt Nam",
            ViewCount = 2150000,
            IsTrending = false,
            IsFeatured = false,
            Director = "Lưu Thành Luân",
            Cast = ["Quang Tuấn", "Nam Thư", "NSND Kim Xuân", "Vân Dung"],
            TotalEpisodes = 1,
            CurrentEpisode = 1,
            Episodes =
            [
                new() { Id = 1501, MovieId = 15, EpisodeNumber = 1, Title = "Bản Chiếu Rạp", DurationMinutes = 105, ThumbnailUrl = "/images/movies/movie-15-backdrop.webp", Streams = [new() { ServerName = "VIP FastServer 1", Quality = "1080p FHD", StreamUrl = "https://vjs.zencdn.net/v/oceans.mp4" }] }
            ]
        },

        // ==========================================
        // 16. ĐẤT RỪNG PHƯƠNG NAM - Phim Lẻ
        // ==========================================
        new()
        {
            Id = 16,
            Title = "Đất Rừng Phương Nam",
            OriginalTitle = "Song of the South",
            Slug = "dat-rung-phuong-nam",
            Overview = "Hành trình phiêu lưu tìm cha đầy gian nan nhưng thấm đượm tình người của cậu bé An giữa miền Tây Nam Bộ hào hùng thời kỳ kháng chiến.",
            PosterUrl = "/images/movies/movie-16-poster.webp",
            BackdropUrl = "/images/movies/movie-16-backdrop.webp",
            TrailerUrl = "https://www.youtube.com/watch?v=hktzirCnJmQ",
            Rating = 7.8,
            VoteCount = 98000,
            ReleaseYear = 2023,
            DurationMinutes = 134,
            AgeRating = "13+",
            Quality = "4K UHD",
            Type = MovieType.Movie,
            Genres = ["Phiêu Lưu", "Lịch Sử", "Kịch Tính"],
            Country = "Việt Nam",
            ViewCount = 3200000,
            IsTrending = false,
            IsFeatured = false,
            Director = "Nguyễn Quang Dũng",
            Cast = ["Hạo Khang", "Tuấn Trần", "Trấn Thành", "Tiến Luật"],
            TotalEpisodes = 1,
            CurrentEpisode = 1,
            Episodes =
            [
                new() { Id = 1601, MovieId = 16, EpisodeNumber = 1, Title = "Bản Điện Ảnh", DurationMinutes = 134, ThumbnailUrl = "/images/movies/movie-16-backdrop.webp", Streams = [new() { ServerName = "VIP FastServer 1", Quality = "4K UHD", StreamUrl = "https://media.w3.org/2010/05/sintel/trailer.mp4" }] }
            ]
        },

        // ==========================================
        // 17. EXHUMA: QUẬT MỘ TRÙNG MA (HÀN QUỐC) - Phim Lẻ
        // ==========================================
        new()
        {
            Id = 17,
            Title = "Exhuma: Quật Mộ Trùng Ma",
            OriginalTitle = "Exhuma",
            Slug = "exhuma-quat-mo-trung-ma",
            Overview = "Hai pháp sư trừ tà trẻ tuổi cùng thầy phong thủy và chuyên gia mai táng nhận lời khai quật ngôi mộ tổ tiên của một gia tộc giàu có, vô tình giải phóng ác quỷ cổ xưa.",
            PosterUrl = "/images/movies/movie-17-poster.webp",
            BackdropUrl = "/images/movies/movie-17-backdrop.webp",
            TrailerUrl = "https://www.youtube.com/watch?v=66K9-l0EkE0",
            Rating = 8.4,
            VoteCount = 189000,
            ReleaseYear = 2024,
            DurationMinutes = 134,
            AgeRating = "16+",
            Quality = "4K HDR",
            Type = MovieType.Movie,
            Genres = ["Kinh Dị", "Kỳ Ảo", "Kịch Tính"],
            Country = "Hàn Quốc",
            ViewCount = 3920000,
            IsTrending = true,
            IsFeatured = false,
            Director = "Jang Jae-hyun",
            Cast = ["Choi Min-sik", "Kim Go-eun", "Yoo Hae-jin", "Lee Do-hyun"],
            TotalEpisodes = 1,
            CurrentEpisode = 1,
            Episodes =
            [
                new() { Id = 1701, MovieId = 17, EpisodeNumber = 1, Title = "Bản Rạp Full HD", DurationMinutes = 134, ThumbnailUrl = "/images/movies/movie-17-backdrop.webp", Streams = [new() { ServerName = "VIP FastServer 1", Quality = "4K UHD", StreamUrl = "https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-1080p.mp4" }] }
            ]
        },

        // ==========================================
        // 18. SQUID GAME MÙA 2 (HÀN QUỐC) - Series
        // ==========================================
        new()
        {
            Id = 18,
            Title = "Squid Game: Trò Chơi Con Mực (Mùa 2)",
            OriginalTitle = "Squid Game Season 2",
            Slug = "squid-game-season-2",
            Overview = "Seong Gi-hun từ bỏ việc sang Mỹ, quyết tâm thâm nhập lại vào đấu trường trò chơi sinh tử bí ẩn để vạch trần tổ chức tàn bạo đứng sau.",
            PosterUrl = "/images/movies/movie-18-poster.webp",
            BackdropUrl = "/images/movies/movie-18-backdrop.webp",
            TrailerUrl = "https://www.youtube.com/watch?v=Ed1sGgHUo88",
            Rating = 8.7,
            VoteCount = 340000,
            ReleaseYear = 2024,
            DurationMinutes = 55,
            AgeRating = "18+",
            Quality = "4K Dolby Vision",
            Type = MovieType.Series,
            Genres = ["Kịch Tính", "Hành Động", "Tội Phạm"],
            Country = "Hàn Quốc",
            ViewCount = 6200000,
            IsTrending = true,
            IsFeatured = false,
            Director = "Hwang Dong-hyuk",
            Cast = ["Lee Jung-jae", "Lee Byung-hun", "Wi Ha-joon", "Yim Si-wan"],
            TotalEpisodes = 6,
            CurrentEpisode = 6,
            Episodes =
            [
                new() { Id = 1801, MovieId = 18, EpisodeNumber = 1, Title = "Tập 1: Bánh Mì Và Xổ Số", DurationMinutes = 55, ThumbnailUrl = "/images/movies/movie-18-backdrop.webp", Streams = [new() { ServerName = "VIP FastServer 1", Quality = "4K UHD", StreamUrl = "https://www.w3schools.com/html/mov_bbb.mp4" }] }
            ]
        },

        // ==========================================
        // 19. QUEEN OF TEARS (HÀN QUỐC) - Series
        // ==========================================
        new()
        {
            Id = 19,
            Title = "Nữ Hoàng Nước Mắt (Queen of Tears)",
            OriginalTitle = "Queen of Tears",
            Slug = "queen-of-tears",
            Overview = "Cuộc hôn nhân khủng hoảng và sự hồi sinh diệu kỳ trong tình yêu giữa nữ tài phiệt kiêu kỳ và chàng luật sư tỉnh lẻ tài năng.",
            PosterUrl = "/images/movies/movie-19-poster.webp",
            BackdropUrl = "/images/movies/movie-19-backdrop.webp",
            TrailerUrl = "https://www.youtube.com/watch?v=Gg2D8zrzlOA",
            Rating = 8.6,
            VoteCount = 210000,
            ReleaseYear = 2024,
            DurationMinutes = 75,
            AgeRating = "16+",
            Quality = "Full HD 1080p",
            Type = MovieType.Series,
            Genres = ["Lãng Mạn", "Kịch Tính", "Hài Hước"],
            Country = "Hàn Quốc",
            ViewCount = 5100000,
            IsTrending = true,
            IsFeatured = false,
            Director = "Jang Young-woo, Kim Hee-won",
            Cast = ["Kim Soo-hyun", "Kim Ji-won", "Park Sung-hoon", "Kwak Dong-yeon"],
            TotalEpisodes = 16,
            CurrentEpisode = 16,
            Episodes =
            [
                new() { Id = 1901, MovieId = 19, EpisodeNumber = 1, Title = "Tập 1: Khủng Hoảng Hôn Nhân", DurationMinutes = 75, ThumbnailUrl = "/images/movies/movie-19-backdrop.webp", Streams = [new() { ServerName = "VIP FastServer 1", Quality = "1080p FHD", StreamUrl = "https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-720p.mp4" }] }
            ]
        },

        // ==========================================
        // 20. TRAIN TO BUSAN (HÀN QUỐC) - Phim Lẻ
        // ==========================================
        new()
        {
            Id = 20,
            Title = "Chuyến Tàu Sinh Tử",
            OriginalTitle = "Train to Busan",
            Slug = "train-to-busan",
            Overview = "Khi dịch bệnh xác sống bùng phát dữ dội tại Hàn Quốc, các hành khách trên chuyến tàu cao tốc từ Seoul tới Busan phải chiến đấu sinh tử để sống sót.",
            PosterUrl = "/images/movies/movie-20-poster.webp",
            BackdropUrl = "/images/movies/movie-20-backdrop.webp",
            TrailerUrl = "https://www.youtube.com/watch?v=pyWuHv2-Abk",
            Rating = 8.6,
            VoteCount = 310000,
            ReleaseYear = 2016,
            DurationMinutes = 118,
            AgeRating = "16+",
            Quality = "Full HD 1080p",
            Type = MovieType.Movie,
            Genres = ["Kinh Dị", "Hành Động", "Kịch Tính"],
            Country = "Hàn Quốc",
            ViewCount = 4900000,
            IsTrending = false,
            IsFeatured = false,
            Director = "Yeon Sang-ho",
            Cast = ["Gong Yoo", "Jung Yu-mi", "Ma Dong-seok", "Kim Su-an"],
            TotalEpisodes = 1,
            CurrentEpisode = 1,
            Episodes =
            [
                new() { Id = 2001, MovieId = 20, EpisodeNumber = 1, Title = "Bản Chuẩn Chiếu Rạp", DurationMinutes = 118, ThumbnailUrl = "/images/movies/movie-20-backdrop.webp", Streams = [new() { ServerName = "VIP FastServer 1", Quality = "1080p FHD", StreamUrl = "https://media.w3.org/2010/05/sintel/trailer.mp4" }] }
            ]
        },

        // ==========================================
        // 21. JOHN WICK 4 - Phim Lẻ
        // ==========================================
        new()
        {
            Id = 21,
            Title = "Sát Thủ John Wick: Phần 4",
            OriginalTitle = "John Wick: Chapter 4",
            Slug = "john-wick-chapter-4",
            Overview = "John Wick tìm ra con đường đánh bại Hội Đồng Tối Cao nhưng phải đối đầu với một kẻ thù mới có liên minh hùng mạnh trên toàn cầu.",
            PosterUrl = "/images/movies/movie-21-poster.webp",
            BackdropUrl = "/images/movies/movie-21-backdrop.webp",
            TrailerUrl = "https://www.youtube.com/watch?v=qEVUtrk8_B4",
            Rating = 8.6,
            VoteCount = 370000,
            ReleaseYear = 2023,
            DurationMinutes = 169,
            AgeRating = "18+",
            Quality = "4K UHD",
            Type = MovieType.Movie,
            Genres = ["Hành Động", "Tội Phạm", "Kịch Tính"],
            Country = "Âu Mỹ",
            ViewCount = 5600000,
            IsTrending = true,
            IsFeatured = false,
            Director = "Chad Stahelski",
            Cast = ["Keanu Reeves", "Donnie Yen", "Bill Skarsgård", "Hiroyuki Sanada"],
            TotalEpisodes = 1,
            CurrentEpisode = 1,
            Episodes =
            [
                new() { Id = 2101, MovieId = 21, EpisodeNumber = 1, Title = "Bản Chiếu Rạp 4K", DurationMinutes = 169, ThumbnailUrl = "/images/movies/movie-21-backdrop.webp", Streams = [new() { ServerName = "VIP FastServer 1", Quality = "4K UHD", StreamUrl = "https://vjs.zencdn.net/v/oceans.mp4" }] }
            ]
        },

        // ==========================================
        // 22. SPIDER-MAN: ACROSS THE SPIDER-VERSE - Phim Lẻ
        // ==========================================
        new()
        {
            Id = 22,
            Title = "Người Nhện: Du Hành Vũ Trụ Nhện",
            OriginalTitle = "Spider-Man: Across the Spider-Verse",
            Slug = "spider-man-across-the-spider-verse",
            Overview = "Miles Morales du hành qua đa vũ trụ nơi cậu gặp gỡ một đội ngũ Người Nhện ưu tú nhưng phải định nghĩa lại ý nghĩa của việc trở thành anh hùng.",
            PosterUrl = "/images/movies/movie-22-poster.webp",
            BackdropUrl = "/images/movies/movie-22-backdrop.webp",
            TrailerUrl = "https://www.youtube.com/watch?v=cqGjhVJWtEg",
            Rating = 8.9,
            VoteCount = 390000,
            ReleaseYear = 2023,
            DurationMinutes = 140,
            AgeRating = "13+",
            Quality = "4K Dolby Vision",
            Type = MovieType.Movie,
            Genres = ["Hoạt Hình", "Hành Động", "Khoa Học Viễn Tưởng", "Phiêu Lưu"],
            Country = "Âu Mỹ",
            ViewCount = 5300000,
            IsTrending = true,
            IsFeatured = false,
            Director = "Joaquim Dos Santos, Kemp Powers",
            Cast = ["Shameik Moore", "Hailee Steinfeld", "Oscar Isaac", "Daniel Kaluuya"],
            TotalEpisodes = 1,
            CurrentEpisode = 1,
            Episodes =
            [
                new() { Id = 2201, MovieId = 22, EpisodeNumber = 1, Title = "Bản Chuẩn Hoạt Hình 4K", DurationMinutes = 140, ThumbnailUrl = "/images/movies/movie-22-backdrop.webp", Streams = [new() { ServerName = "VIP FastServer 1", Quality = "4K UHD", StreamUrl = "https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-1080p.mp4" }] }
            ]
        },

        // ==========================================
        // 23. TOP GUN: MAVERICK - Phim Lẻ
        // ==========================================
        new()
        {
            Id = 23,
            Title = "Phi Công Siêu Đẳng Maverick",
            OriginalTitle = "Top Gun: Maverick",
            Slug = "top-gun-maverick",
            Overview = "Sau hơn 30 năm phục vụ, Pete Mitchell dẫn dắt một nhóm phi công trẻ tinh nhuệ thực hiện một phi vụ không kích nguy hiểm tột độ.",
            PosterUrl = "/images/movies/movie-23-poster.webp",
            BackdropUrl = "/images/movies/movie-23-backdrop.webp",
            TrailerUrl = "https://www.youtube.com/watch?v=giXco2jaZ_4",
            Rating = 8.7,
            VoteCount = 350000,
            ReleaseYear = 2022,
            DurationMinutes = 130,
            AgeRating = "13+",
            Quality = "4K IMAX",
            Type = MovieType.Movie,
            Genres = ["Hành Động", "Kịch Tính", "Phiêu Lưu"],
            Country = "Âu Mỹ",
            ViewCount = 4700000,
            IsTrending = false,
            IsFeatured = false,
            Director = "Joseph Kosinski",
            Cast = ["Tom Cruise", "Miles Teller", "Jennifer Connelly", "Jon Hamm"],
            TotalEpisodes = 1,
            CurrentEpisode = 1,
            Episodes =
            [
                new() { Id = 2301, MovieId = 23, EpisodeNumber = 1, Title = "Bản Chiếu Rạp IMAX 4K", DurationMinutes = 130, ThumbnailUrl = "/images/movies/movie-23-backdrop.webp", Streams = [new() { ServerName = "VIP FastServer 1", Quality = "4K UHD", StreamUrl = "https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-720p.mp4" }] }
            ]
        },

        // ==========================================
        // 24. AVATAR: THE WAY OF WATER - Phim Lẻ
        // ==========================================
        new()
        {
            Id = 24,
            Title = "Avatar: Dòng Chảy Của Nước",
            OriginalTitle = "Avatar: The Way of Water",
            Slug = "avatar-the-way-of-water",
            Overview = "Jake Sully và Neytiri cùng các con phải rời bỏ rừng già, tìm nơi ẩn náu tại các bộ tộc sống ven biển Pandora khi Người Trời quay lại xâm chiếm.",
            PosterUrl = "/images/movies/movie-24-poster.webp",
            BackdropUrl = "/images/movies/movie-24-backdrop.webp",
            TrailerUrl = "https://www.youtube.com/watch?v=d9MyW72ELq0",
            Rating = 8.5,
            VoteCount = 410000,
            ReleaseYear = 2022,
            DurationMinutes = 192,
            AgeRating = "13+",
            Quality = "4K 3D HFR",
            Type = MovieType.Movie,
            Genres = ["Khoa Học Viễn Tưởng", "Phiêu Lưu", "Kỳ Ảo"],
            Country = "Âu Mỹ",
            ViewCount = 6800000,
            IsTrending = false,
            IsFeatured = false,
            Director = "James Cameron",
            Cast = ["Sam Worthington", "Zoe Saldana", "Sigourney Weaver", "Stephen Lang"],
            TotalEpisodes = 1,
            CurrentEpisode = 1,
            Episodes =
            [
                new() { Id = 2401, MovieId = 24, EpisodeNumber = 1, Title = "Bản Chiếu Rạp 4K UHD", DurationMinutes = 192, ThumbnailUrl = "/images/movies/movie-24-backdrop.webp", Streams = [new() { ServerName = "VIP FastServer 1", Quality = "4K UHD", StreamUrl = "https://media.w3.org/2010/05/sintel/trailer.mp4" }] }
            ]
        },

        // ==========================================
        // 25. THE LAST OF US (MÙA 1) - Series
        // ==========================================
        new()
        {
            Id = 25,
            Title = "The Last of Us: Những Người Còn Sót Lại",
            OriginalTitle = "The Last of Us",
            Slug = "the-last-of-us",
            Overview = "20 năm sau khi đại dịch nấm Cordyceps tàn phá văn minh, người đàn ông chai sạn Joel được thuê đưa cô bé 14 tuổi Ellie vượt qua nước Mỹ hoang tàn.",
            PosterUrl = "/images/movies/movie-25-poster.webp",
            BackdropUrl = "/images/movies/movie-25-backdrop.webp",
            TrailerUrl = "https://www.youtube.com/watch?v=uLtkt8BonwM",
            Rating = 8.9,
            VoteCount = 320000,
            ReleaseYear = 2023,
            DurationMinutes = 60,
            AgeRating = "18+",
            Quality = "4K HDR",
            Type = MovieType.Series,
            Genres = ["Kịch Tính", "Hành Động", "Kinh Dị", "Khoa Học Viễn Tưởng"],
            Country = "Âu Mỹ",
            ViewCount = 5400000,
            IsTrending = true,
            IsFeatured = false,
            Director = "Craig Mazin, Neil Druckmann",
            Cast = ["Pedro Pascal", "Bella Ramsey", "Gabriel Luna", "Anna Torv"],
            TotalEpisodes = 9,
            CurrentEpisode = 9,
            Episodes =
            [
                new() { Id = 2501, MovieId = 25, EpisodeNumber = 1, Title = "Tập 1: Khi Bạn Lạc Trong Bóng Tối", DurationMinutes = 81, ThumbnailUrl = "/images/movies/movie-25-backdrop.webp", Streams = [new() { ServerName = "VIP FastServer 1", Quality = "4K UHD", StreamUrl = "https://www.w3schools.com/html/mov_bbb.mp4" }] }
            ]
        },

        // ==========================================
        // 26. BREAKING BAD - Series
        // ==========================================
        new()
        {
            Id = 26,
            Title = "Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)",
            OriginalTitle = "Breaking Bad",
            Slug = "breaking-bad",
            Overview = "Một giáo viên hóa học trung học được chẩn đoán mắc bệnh ung thư giai đoạn cuối đã hợp tác với cựu học sinh để chế tạo ma túy đá nhằm bảo đảm tương lai cho gia đình.",
            PosterUrl = "/images/movies/movie-26-poster.webp",
            BackdropUrl = "/images/movies/movie-26-backdrop.webp",
            TrailerUrl = "https://www.youtube.com/watch?v=HhesaQXLuRY",
            Rating = 9.5,
            VoteCount = 540000,
            ReleaseYear = 2013,
            DurationMinutes = 48,
            AgeRating = "18+",
            Quality = "4K Remastered",
            Type = MovieType.Series,
            Genres = ["Tội Phạm", "Kịch Tính", "Tâm Lý"],
            Country = "Âu Mỹ",
            ViewCount = 7200000,
            IsTrending = false,
            IsFeatured = false,
            Director = "Vince Gilligan",
            Cast = ["Bryan Cranston", "Aaron Paul", "Anna Gunn", "Bob Odenkirk"],
            TotalEpisodes = 62,
            CurrentEpisode = 62,
            Episodes =
            [
                new() { Id = 2601, MovieId = 26, EpisodeNumber = 1, Title = "Tập 1: Khởi Đầu Của Heisenberg", DurationMinutes = 58, ThumbnailUrl = "/images/movies/movie-26-backdrop.webp", Streams = [new() { ServerName = "VIP FastServer 1", Quality = "4K UHD", StreamUrl = "https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-1080p.mp4" }] }
            ]
        },

        // ==========================================
        // 27. CUỒNG PHONG (TRUNG QUỐC) - Series
        // ==========================================
        new()
        {
            Id = 27,
            Title = "Cuồng Phong",
            OriginalTitle = "The Knockout",
            Slug = "cuong-phong",
            Overview = "Cuộc đấu trí kéo dài suốt 20 năm giữa cảnh sát hình sự chính trực An Hân và trùm thế lực ngầm Cao Khải Cường tại thành phố Lâm Hải.",
            PosterUrl = "/images/movies/movie-27-poster.webp",
            BackdropUrl = "/images/movies/movie-27-backdrop.webp",
            TrailerUrl = "https://www.youtube.com/watch?v=FicZ1DDQ1Zo",
            Rating = 8.8,
            VoteCount = 280000,
            ReleaseYear = 2023,
            DurationMinutes = 45,
            AgeRating = "16+",
            Quality = "Full HD 1080p",
            Type = MovieType.Series,
            Genres = ["Tội Phạm", "Kịch Tính", "Hành Động"],
            Country = "Trung Quốc",
            ViewCount = 4300000,
            IsTrending = false,
            IsFeatured = false,
            Director = "Từ Kỷ Chu",
            Cast = ["Trương Dịch", "Trương Tụng Văn", "Lý Nhất Đồng", "Trương Chí Kiên"],
            TotalEpisodes = 39,
            CurrentEpisode = 39,
            Episodes =
            [
                new() { Id = 2701, MovieId = 27, EpisodeNumber = 1, Title = "Tập 1: Đêm Trừ Tịch", DurationMinutes = 45, ThumbnailUrl = "/images/movies/movie-27-backdrop.webp", Streams = [new() { ServerName = "VIP FastServer 1", Quality = "1080p FHD", StreamUrl = "https://vjs.zencdn.net/v/oceans.mp4" }] }
            ]
        },

        // ==========================================
        // 28. ĐI ĐẾN NƠI CÓ GIÓ (TRUNG QUỐC) - Series
        // ==========================================
        new()
        {
            Id = 28,
            Title = "Đi Đến Nơi Có Gió",
            OriginalTitle = "Meet Yourself",
            Slug = "di-den-noi-co-gio",
            Overview = "Hứa Hồng Đậu sau cú sốc mất bạn thân đã đến thôn Vân Miêu chữa lành tâm hồn, tìm lại ý nghĩa cuộc sống và tình yêu bên Tạ Chi Dao.",
            PosterUrl = "/images/movies/movie-28-poster.webp",
            BackdropUrl = "/images/movies/movie-28-backdrop.webp",
            TrailerUrl = "https://www.youtube.com/watch?v=9vHlPCF1idk",
            Rating = 8.6,
            VoteCount = 195000,
            ReleaseYear = 2023,
            DurationMinutes = 45,
            AgeRating = "13+",
            Quality = "Full HD 1080p",
            Type = MovieType.Series,
            Genres = ["Lãng Mạn", "Tâm Lý", "Hài Hước"],
            Country = "Trung Quốc",
            ViewCount = 3800000,
            IsTrending = false,
            IsFeatured = false,
            Director = "Đinh Tử Quang",
            Cast = ["Lưu Diệc Phi", "Lý Hiện", "Hồ Băng Khanh", "Ngưu Tuấn Phong"],
            TotalEpisodes = 40,
            CurrentEpisode = 40,
            Episodes =
            [
                new() { Id = 2801, MovieId = 28, EpisodeNumber = 1, Title = "Tập 1: Khởi Hành Đến Đại Lý", DurationMinutes = 45, ThumbnailUrl = "/images/movies/movie-28-backdrop.webp", Streams = [new() { ServerName = "VIP FastServer 1", Quality = "1080p FHD", StreamUrl = "https://media.w3.org/2010/05/sintel/trailer.mp4" }] }
            ]
        },

        // ==========================================
        // 29. NGƯỜI DƠI (THE BATMAN) - Phim Lẻ
        // ==========================================
        new()
        {
            Id = 29,
            Title = "Người Dơi (The Batman)",
            OriginalTitle = "The Batman",
            Slug = "the-batman",
            Overview = "Trong năm thứ hai bảo vệ Gotham, Batman lần theo những manh mối bí ẩn của tên sát nhân Riddler nhằm vạch trần tệ nạn tham nhũng ăn sâu vào gia tộc Wayne.",
            PosterUrl = "/images/movies/movie-29-poster.webp",
            BackdropUrl = "/images/movies/movie-29-backdrop.webp",
            TrailerUrl = "https://www.youtube.com/watch?v=mqqft2x_Aa4",
            Rating = 8.5,
            VoteCount = 340000,
            ReleaseYear = 2022,
            DurationMinutes = 176,
            AgeRating = "16+",
            Quality = "4K HDR",
            Type = MovieType.Movie,
            Genres = ["Hành Động", "Tội Phạm", "Kịch Tính"],
            Country = "Âu Mỹ",
            ViewCount = 4600000,
            IsTrending = false,
            IsFeatured = false,
            Director = "Matt Reeves",
            Cast = ["Robert Pattinson", "Zoë Kravitz", "Paul Dano", "Jeffrey Wright"],
            TotalEpisodes = 1,
            CurrentEpisode = 1,
            Episodes =
            [
                new() { Id = 2901, MovieId = 29, EpisodeNumber = 1, Title = "Bản Chuẩn 4K", DurationMinutes = 176, ThumbnailUrl = "/images/movies/movie-29-backdrop.webp", Streams = [new() { ServerName = "VIP FastServer 1", Quality = "4K UHD", StreamUrl = "https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-1080p.mp4" }] }
            ]
        },

        // ==========================================
        // 30. PHONG THẦN 1: YÊU LOẠN THƯƠNG CHU - Phim Lẻ
        // ==========================================
        new()
        {
            Id = 30,
            Title = "Phong Thần Tam Bộ Khúc: Yêu Loạn Thương Chu",
            OriginalTitle = "Creation of the Gods I: Kingdom of Storms",
            Slug = "phong-than-1",
            Overview = "Sử thi kỳ ảo hoành tráng tái hiện cuộc đối đầu huyền thoại giữa Trụ Vương, Đát Kỷ và các dũng sĩ Cơ Phát, Khương Tử Nha thời cổ đại.",
            PosterUrl = "/images/movies/movie-30-poster.webp",
            BackdropUrl = "/images/movies/movie-30-backdrop.webp",
            TrailerUrl = "https://www.youtube.com/watch?v=_a8j3NUJiQ0",
            Rating = 8.3,
            VoteCount = 185000,
            ReleaseYear = 2023,
            DurationMinutes = 148,
            AgeRating = "16+",
            Quality = "4K IMAX",
            Type = MovieType.Movie,
            Genres = ["Kỳ Ảo", "Hành Động", "Lịch Sử", "Phiêu Lưu"],
            Country = "Trung Quốc",
            ViewCount = 3500000,
            IsTrending = false,
            IsFeatured = false,
            Director = "Ô Nhĩ Thiện",
            Cast = ["Phí Tường", "Lý Tuyết Kiện", "Hoàng Bột", "Naran"],
            TotalEpisodes = 1,
            CurrentEpisode = 1,
            Episodes =
            [
                new() { Id = 3001, MovieId = 30, EpisodeNumber = 1, Title = "Bản Chiếu Rạp IMAX", DurationMinutes = 148, ThumbnailUrl = "/images/movies/movie-30-backdrop.webp", Streams = [new() { ServerName = "VIP FastServer 1", Quality = "4K UHD", StreamUrl = "https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-720p.mp4" }] }
            ]
        },

        // ==========================================
        // 31. KIMETSU NO YAIBA: HASHIRA TRAINING - Anime
        // ==========================================
        new()
        {
            Id = 31,
            Title = "Thanh Gươm Diệt Quỷ: Đại Trụ Huấn Luyện",
            OriginalTitle = "Demon Slayer: Kimetsu no Yaiba - Hashira Training Arc",
            Slug = "kimetsu-no-yaiba-hashira-training",
            Overview = "Tanjiro cùng Sát Quỷ Đoàn tham gia khóa huấn luyện khắt khe cùng các Đại Trụ để chuẩn bị cho trận đại chiến cuối cùng chống lại Chúa Quỷ Muzan.",
            PosterUrl = "/images/movies/movie-31-poster.webp",
            BackdropUrl = "/images/movies/movie-31-backdrop.webp",
            TrailerUrl = "https://www.youtube.com/watch?v=a9tq0aS5Zu8",
            Rating = 8.9,
            VoteCount = 265000,
            ReleaseYear = 2024,
            DurationMinutes = 24,
            AgeRating = "16+",
            Quality = "Full HD 1080p",
            Type = MovieType.Anime,
            Genres = ["Anime", "Hành Động", "Kỳ Ảo"],
            Country = "Nhật Bản",
            ViewCount = 5800000,
            IsTrending = true,
            IsFeatured = false,
            Director = "Haruo Sotozaki",
            Cast = ["Natsuki Hanae", "Akari Kito", "Hiro Shimono", "Yoshitsugu Matsuoka"],
            TotalEpisodes = 8,
            CurrentEpisode = 8,
            Episodes =
            [
                new() { Id = 3101, MovieId = 31, EpisodeNumber = 1, Title = "Tập 1: Để Đánh Bại Kibutsuji Muzan", DurationMinutes = 48, ThumbnailUrl = "/images/movies/movie-31-backdrop.webp", Streams = [new() { ServerName = "VIP FastServer 1", Quality = "1080p 60fps", StreamUrl = "https://vjs.zencdn.net/v/oceans.mp4" }] }
            ]
        },

        // ==========================================
        // 32. ATTACK ON TITAN: THE FINAL CHAPTERS - Anime
        // ==========================================
        new()
        {
            Id = 32,
            Title = "Đại Chiến Titan: Hồi Kết Tuyệt Đối",
            OriginalTitle = "Attack on Titan: The Final Chapters",
            Slug = "attack-on-titan-final-chapters",
            Overview = "Eren Yeager kích hoạt cuộc Rung Chấn hủy diệt thế giới, buộc những đồng đội cũ phải liên minh để đối đầu với anh trong trận chiến định đoạt số phận nhân loại.",
            PosterUrl = "/images/movies/movie-32-poster.webp",
            BackdropUrl = "/images/movies/movie-32-backdrop.webp",
            TrailerUrl = "https://www.youtube.com/watch?v=3xNH23QkNpk",
            Rating = 9.2,
            VoteCount = 380000,
            ReleaseYear = 2023,
            DurationMinutes = 85,
            AgeRating = "18+",
            Quality = "Full HD 1080p",
            Type = MovieType.Anime,
            Genres = ["Anime", "Hành Động", "Kịch Tính", "Kỳ Ảo"],
            Country = "Nhật Bản",
            ViewCount = 6900000,
            IsTrending = true,
            IsFeatured = false,
            Director = "Yuichiro Hayashi",
            Cast = ["Yuki Kaji", "Yui Ishikawa", "Marina Inoue", "Hiroshi Kamiya"],
            TotalEpisodes = 2,
            CurrentEpisode = 2,
            Episodes =
            [
                new() { Id = 3201, MovieId = 32, EpisodeNumber = 1, Title = "Tập 1: Tiếng Gầm Của Tự Do", DurationMinutes = 61, ThumbnailUrl = "/images/movies/movie-32-backdrop.webp", Streams = [new() { ServerName = "VIP FastServer 1", Quality = "1080p 60fps", StreamUrl = "https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-1080p.mp4" }] }
            ]
        },

        // ==========================================
        // 33. FRIEREN: PHÁP SƯ TIỄN TÁNG - Anime
        // ==========================================
        new()
        {
            Id = 33,
            Title = "Frieren: Pháp Sư Tiễn Táng",
            OriginalTitle = "Frieren: Beyond Journey's End",
            Slug = "frieren-beyond-journeys-end",
            Overview = "Sau khi đánh bại Quỷ Vương, pháp sư tộc elf Frieren bất tử bắt đầu hành trình mới để thấu hiểu con người và những ký ức vô giá về các chiến hữu đã khuất.",
            PosterUrl = "/images/movies/movie-33-poster.webp",
            BackdropUrl = "/images/movies/movie-33-backdrop.webp",
            TrailerUrl = "https://www.youtube.com/watch?v=Iwr1aLEDpe4",
            Rating = 9.3,
            VoteCount = 295000,
            ReleaseYear = 2024,
            DurationMinutes = 24,
            AgeRating = "13+",
            Quality = "Full HD 1080p",
            Type = MovieType.Anime,
            Genres = ["Anime", "Kỳ Ảo", "Phiêu Lưu", "Kịch Tính"],
            Country = "Nhật Bản",
            ViewCount = 4600000,
            IsTrending = true,
            IsFeatured = false,
            Director = "Keiichiro Saito",
            Cast = ["Atsumi Tanezaki", "Nobuhiko Okamoto", "Hiroki Touchi", "Kana Ichinose"],
            TotalEpisodes = 28,
            CurrentEpisode = 28,
            Episodes =
            [
                new() { Id = 3301, MovieId = 33, EpisodeNumber = 1, Title = "Tập 1: Kết Thúc Của Cuộc Phiêu Lưu", DurationMinutes = 24, ThumbnailUrl = "/images/movies/movie-33-backdrop.webp", Streams = [new() { ServerName = "VIP FastServer 1", Quality = "1080p 60fps", StreamUrl = "https://media.w3.org/2010/05/sintel/trailer.mp4" }] }
            ]
        },

        // ==========================================
        // 34. CHAINSAW MAN - Anime
        // ==========================================
        new()
        {
            Id = 34,
            Title = "Thợ Săn Quỷ Chainsaw Man",
            OriginalTitle = "Chainsaw Man",
            Slug = "chainsaw-man",
            Overview = "Denji là một thanh niên nghèo khó hồi sinh dưới dạng Người Cưa sau khi hợp nhất với quỷ Pochita, gia nhập tổ chức thợ săn quỷ an toàn công cộng của Makima.",
            PosterUrl = "/images/movies/movie-34-poster.webp",
            BackdropUrl = "/images/movies/movie-34-backdrop.webp",
            TrailerUrl = "https://www.youtube.com/watch?v=q15CRdE5Bv0",
            Rating = 8.6,
            VoteCount = 230000,
            ReleaseYear = 2022,
            DurationMinutes = 24,
            AgeRating = "18+",
            Quality = "Full HD 1080p",
            Type = MovieType.Anime,
            Genres = ["Anime", "Hành Động", "Kinh Dị", "Kỳ Ảo"],
            Country = "Nhật Bản",
            ViewCount = 4200000,
            IsTrending = false,
            IsFeatured = false,
            Director = "Ryu Nakayama",
            Cast = ["Kikunosuke Toya", "Tomori Kusunoki", "Shogo Sakata", "Fairouz Ai"],
            TotalEpisodes = 12,
            CurrentEpisode = 12,
            Episodes =
            [
                new() { Id = 3401, MovieId = 34, EpisodeNumber = 1, Title = "Tập 1: Chó Và Cưa", DurationMinutes = 24, ThumbnailUrl = "/images/movies/movie-34-backdrop.webp", Streams = [new() { ServerName = "VIP FastServer 1", Quality = "1080p 60fps", StreamUrl = "https://vjs.zencdn.net/v/oceans.mp4" }] }
            ]
        },

        // ==========================================
        // 35. YOUR NAME (KIMI NO NA WA) - Anime
        // ==========================================
        new()
        {
            Id = 35,
            Title = "Tên Cậu Là Gì? (Your Name)",
            OriginalTitle = "Kimi no Na wa.",
            Slug = "your-name",
            Overview = "Hai thiếu niên xa lạ sống ở Tokyo và vùng nông thôn hẻo lánh bỗng hoán đổi thân xác trong giấc mơ, cùng chạy đua với thời gian để ngăn thảm họa sao băng.",
            PosterUrl = "/images/movies/movie-35-poster.webp",
            BackdropUrl = "/images/movies/movie-35-backdrop.webp",
            TrailerUrl = "https://www.youtube.com/watch?v=xU47nhruN-Q",
            Rating = 8.9,
            VoteCount = 385000,
            ReleaseYear = 2016,
            DurationMinutes = 106,
            AgeRating = "13+",
            Quality = "4K Remastered",
            Type = MovieType.Anime,
            Genres = ["Anime", "Lãng Mạn", "Kỳ Ảo", "Hoạt Hình"],
            Country = "Nhật Bản",
            ViewCount = 6400000,
            IsTrending = false,
            IsFeatured = false,
            Director = "Makoto Shinkai",
            Cast = ["Ryunosuke Kamiki", "Mone Kamishiraishi", "Ryo Narita"],
            TotalEpisodes = 1,
            CurrentEpisode = 1,
            Episodes =
            [
                new() { Id = 3501, MovieId = 35, EpisodeNumber = 1, Title = "Bản Điện Ảnh Full HD", DurationMinutes = 106, ThumbnailUrl = "/images/movies/movie-35-backdrop.webp", Streams = [new() { ServerName = "VIP FastServer 1", Quality = "4K UHD", StreamUrl = "https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-1080p.mp4" }] }
            ]
        },

        // ==========================================
        // 36. SPIRITED AWAY (VÙNG ĐẤT LINH HỒN) - Anime
        // ==========================================
        new()
        {
            Id = 36,
            Title = "Vùng Đất Linh Hồn (Spirited Away)",
            OriginalTitle = "Sen to Chihiro no Kamikakushi",
            Slug = "spirited-away",
            Overview = "Cô bé 10 tuổi Chihiro lạc vào thế giới của các vị thần linh và phù thủy, phải dũng cảm làm việc tại nhà tắm hơi bí ẩn để giải cứu cha mẹ.",
            PosterUrl = "/images/movies/movie-36-poster.webp",
            BackdropUrl = "/images/movies/movie-36-backdrop.webp",
            TrailerUrl = "https://www.youtube.com/watch?v=ByXuk9QqQkk",
            Rating = 9.0,
            VoteCount = 450000,
            ReleaseYear = 2001,
            DurationMinutes = 125,
            AgeRating = "G",
            Quality = "Full HD Remaster",
            Type = MovieType.Anime,
            Genres = ["Anime", "Hoạt Hình", "Phiêu Lưu", "Kỳ Ảo"],
            Country = "Nhật Bản",
            ViewCount = 7800000,
            IsTrending = false,
            IsFeatured = false,
            Director = "Hayao Miyazaki",
            Cast = ["Rumi Hiiragi", "Miyu Irino", "Mari Natsuki", "Takashi Naito"],
            TotalEpisodes = 1,
            CurrentEpisode = 1,
            Episodes =
            [
                new() { Id = 3601, MovieId = 36, EpisodeNumber = 1, Title = "Bản Đoạt Giải Oscar", DurationMinutes = 125, ThumbnailUrl = "/images/movies/movie-36-backdrop.webp", Streams = [new() { ServerName = "VIP FastServer 1", Quality = "1080p FHD", StreamUrl = "https://media.w3.org/2010/05/sintel/trailer.mp4" }] }
            ]
        },

        // ==========================================
        // 37. VÀNH ĐAI ĐỎ - Khoa học viễn tưởng
        // ==========================================
        new()
        {
            Id = 37,
            Title = "Vành Đai Đỏ",
            OriginalTitle = "The Red Orbit",
            Slug = "vanh-dai-do",
            Overview = "Một phi hành đoàn cứu hộ nhận được tín hiệu bí ẩn từ trạm nghiên cứu bỏ hoang ngoài quỹ đạo Sao Hỏa. Khi tiếp cận, họ phát hiện ký ức của chính mình đang bị tái tạo bên trong trạm.",
            PosterUrl = "/images/movies/movie-37-poster.webp",
            BackdropUrl = "/images/movies/movie-37-backdrop.webp",
            TrailerUrl = "https://www.youtube.com/watch?v=Yf3VreXwVpI",
            Rating = 8.8,
            VoteCount = 86420,
            ReleaseYear = 2026,
            DurationMinutes = 48,
            AgeRating = "16+",
            Quality = "4K Dolby Vision",
            Type = MovieType.Series,
            Genres = ["Khoa Học Viễn Tưởng", "Kịch Tính", "Phiêu Lưu"],
            Country = "Âu Mỹ",
            ViewCount = 1684000,
            IsTrending = true,
            IsFeatured = true,
            Director = "Lena Hartmann",
            Cast = ["Mara Ellis", "Jonas Vale", "Kei Tanaka", "Amara Cole"],
            TotalEpisodes = 8,
            CurrentEpisode = 8,
            Episodes = BuildEpisodes(
                37,
                48,
                [
                    "Tín Hiệu Từ Phobos",
                    "Khoang Số Bảy",
                    "Bản Sao Trong Gương",
                    "Mười Bảy Phút Mất Tích",
                    "Vùng Tối Quỹ Đạo",
                    "Ký Ức Của Người Chết",
                    "Cửa Thoát Cuối Cùng",
                    "Bình Minh Trên Sao Hỏa"
                ],
                "/images/movies/movie-37-backdrop.webp",
                "/images/movies/movie-37-backdrop.webp",
                "/images/movies/movie-37-backdrop.webp")
        },

        // ==========================================
        // 38. SÀI GÒN SAU NỬA ĐÊM - Phim Việt
        // ==========================================
        new()
        {
            Id = 38,
            Title = "Sài Gòn Sau Nửa Đêm",
            OriginalTitle = "Saigon After Midnight",
            Slug = "sai-gon-sau-nua-dem",
            Overview = "Một nữ phóng viên ảnh lần theo cuộn phim thất lạc và bị cuốn vào mạng lưới bí mật trải dài qua những con hẻm Sài Gòn. Mỗi bức ảnh hé lộ thêm một phần của vụ án đã bị lãng quên.",
            PosterUrl = "/images/movies/movie-38-poster.webp",
            BackdropUrl = "/images/movies/movie-38-backdrop.webp",
            TrailerUrl = "https://www.youtube.com/watch?v=SXiWz0yLLdE",
            Rating = 8.4,
            VoteCount = 52780,
            ReleaseYear = 2025,
            DurationMinutes = 124,
            AgeRating = "18+",
            Quality = "4K UHD",
            Type = MovieType.Movie,
            Genres = ["Tội Phạm", "Tâm Lý", "Kịch Tính"],
            Country = "Việt Nam",
            ViewCount = 932000,
            IsTrending = true,
            IsFeatured = false,
            Director = "Lê Minh Khôi",
            Cast = ["Trần Lam", "Nguyễn Hoàng Phúc", "Bảo Anh", "Khánh Vân"],
            TotalEpisodes = 1,
            CurrentEpisode = 1,
            Episodes = BuildEpisodes(
                38,
                124,
                ["Bản Điện Ảnh"],
                "/images/movies/movie-38-backdrop.webp")
        },

        // ==========================================
        // 39. BIÊN NIÊN SỬ AETHER - Anime
        // ==========================================
        new()
        {
            Id = 39,
            Title = "Biên Niên Sử Aether",
            OriginalTitle = "Aether Chronicle",
            Slug = "bien-nien-su-aether",
            Overview = "Trên những hòn đảo trôi giữa tầng mây, một học viên chế tạo trẻ tuổi vô tình đánh thức cổ vật có thể điều khiển bầu trời và trở thành mục tiêu của đế chế đang thống trị thế giới.",
            PosterUrl = "/images/movies/movie-39-poster.webp",
            BackdropUrl = "/images/movies/movie-39-backdrop.webp",
            TrailerUrl = "https://www.youtube.com/watch?v=iaJ4VVFGIa8",
            Rating = 8.7,
            VoteCount = 121300,
            ReleaseYear = 2026,
            DurationMinutes = 25,
            AgeRating = "13+",
            Quality = "Full HD 1080p",
            Type = MovieType.Anime,
            Genres = ["Anime", "Kỳ Ảo", "Hành Động", "Phiêu Lưu"],
            Country = "Nhật Bản",
            ViewCount = 2140000,
            IsTrending = true,
            IsFeatured = false,
            Director = "Akira Senda",
            Cast = ["Riku Aoyama", "Mio Kisaragi", "Daichi Mori", "Hana Ueda"],
            TotalEpisodes = 10,
            CurrentEpisode = 10,
            Episodes = BuildEpisodes(
                39,
                25,
                [
                    "Đứa Trẻ Của Tầng Mây",
                    "Cổ Vật Thức Giấc",
                    "Bản Đồ Không Có Phương Bắc",
                    "Thành Phố Cánh Buồm",
                    "Kẻ Săn Bão",
                    "Đêm Trên Đảo Trôi",
                    "Trái Tim Của Cỗ Máy",
                    "Cuộc Vây Hãm Trên Không",
                    "Bầu Trời Vỡ Đôi",
                    "Ngày Gió Trở Về"
                ],
                "/images/movies/movie-39-backdrop.webp",
                "/images/movies/movie-39-backdrop.webp",
                "/images/movies/movie-39-backdrop.webp")
        },

        // ==========================================
        // 40. MÙA TUYẾT CUỐI CÙNG - Phim bộ Hàn Quốc
        // ==========================================
        new()
        {
            Id = 40,
            Title = "Mùa Tuyết Cuối Cùng",
            OriginalTitle = "The Last Snowfall",
            Slug = "mua-tuyet-cuoi-cung",
            Overview = "Một kiến trúc sư trở về thị trấn ven biển để khôi phục nhà ga cũ và gặp lại người bạn từng rời đi không lời từ biệt. Mùa đông cuối cùng của thị trấn buộc họ đối diện với những điều chưa từng nói.",
            PosterUrl = "/images/movies/movie-40-poster.webp",
            BackdropUrl = "/images/movies/movie-40-backdrop.webp",
            TrailerUrl = "https://www.youtube.com/watch?v=RYmQHMC8NII",
            Rating = 8.6,
            VoteCount = 74360,
            ReleaseYear = 2025,
            DurationMinutes = 62,
            AgeRating = "13+",
            Quality = "4K HDR",
            Type = MovieType.Series,
            Genres = ["Lãng Mạn", "Tâm Lý", "Kịch Tính"],
            Country = "Hàn Quốc",
            ViewCount = 1475000,
            IsTrending = false,
            IsFeatured = false,
            Director = "Han Seo-jun",
            Cast = ["Kim Ji-won", "Lee Do-hyun", "Park Min-young", "Jung Hae-in"],
            TotalEpisodes = 12,
            CurrentEpisode = 12,
            Episodes = BuildEpisodes(
                40,
                62,
                [
                    "Chuyến Tàu Trở Về",
                    "Ngôi Nhà Có Ánh Đèn",
                    "Bức Thư Chưa Gửi",
                    "Con Đường Ven Biển",
                    "Ngày Tuyết Đầu Mùa",
                    "Lời Hứa Ở Sân Ga",
                    "Khoảng Cách Một Thành Phố",
                    "Căn Phòng Số 203",
                    "Sau Cơn Bão Tuyết",
                    "Điều Chúng Ta Bỏ Lỡ",
                    "Chuyến Tàu Cuối",
                    "Mùa Xuân Đầu Tiên"
                ],
                "/images/movies/movie-40-backdrop.webp",
                "/images/movies/movie-40-backdrop.webp",
                "/images/movies/movie-40-backdrop.webp")
        }
    ];

    public static List<StreamSourceDto> GenerateMultiServerStreams(int seed = 1)
    {
        var urls = new[]
        {
            "https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-1080p.mp4",
            "https://vjs.zencdn.net/v/oceans.mp4",
            "https://media.w3.org/2010/05/sintel/trailer.mp4",
            "https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-720p.mp4",
            "https://www.w3schools.com/html/mov_bbb.mp4"
        };

        var u0 = urls[Math.Abs(seed) % urls.Length];
        var u1 = urls[Math.Abs(seed + 1) % urls.Length];
        var u2 = urls[Math.Abs(seed + 2) % urls.Length];
        var u3 = urls[Math.Abs(seed + 3) % urls.Length];
        var u4 = urls[Math.Abs(seed + 4) % urls.Length];

        return
        [
            // Server 1: VIP 1 (Hà Nội - Siêu Tốc) - 4 qualities
            new() { ServerName = "VIP 1 (Hà Nội - Siêu Tốc)", ServerType = StreamServerType.VipFastServer, Quality = "4K Ultra HD", StreamUrl = u0, Position = 1 },
            new() { ServerName = "VIP 1 (Hà Nội - Siêu Tốc)", ServerType = StreamServerType.VipFastServer, Quality = "1080p 60fps", StreamUrl = u1, Position = 2 },
            new() { ServerName = "VIP 1 (Hà Nội - Siêu Tốc)", ServerType = StreamServerType.VipFastServer, Quality = "1080p FHD", StreamUrl = u2, Position = 3 },
            new() { ServerName = "VIP 1 (Hà Nội - Siêu Tốc)", ServerType = StreamServerType.VipFastServer, Quality = "720p HD", StreamUrl = u3, Position = 4 },

            // Server 2: VIP 2 (Sài Gòn - Ổn Định) - 3 qualities
            new() { ServerName = "VIP 2 (Sài Gòn - Ổn Định)", ServerType = StreamServerType.VipFastServer, Quality = "4K HDR", StreamUrl = u1, Position = 5 },
            new() { ServerName = "VIP 2 (Sài Gòn - Ổn Định)", ServerType = StreamServerType.VipFastServer, Quality = "1080p FHD", StreamUrl = u2, Position = 6 },
            new() { ServerName = "VIP 2 (Sài Gòn - Ổn Định)", ServerType = StreamServerType.VipFastServer, Quality = "720p HD", StreamUrl = u3, Position = 7 },

            // Server 3: Hydra CDN (Quốc Tế) - 3 qualities (no 4K!)
            new() { ServerName = "Hydra CDN (Quốc Tế)", ServerType = StreamServerType.HydraCdn, Quality = "1080p FHD", StreamUrl = u2, Position = 8 },
            new() { ServerName = "Hydra CDN (Quốc Tế)", ServerType = StreamServerType.HydraCdn, Quality = "720p HD", StreamUrl = u3, Position = 9 },
            new() { ServerName = "Hydra CDN (Quốc Tế)", ServerType = StreamServerType.HydraCdn, Quality = "480p SD", StreamUrl = u4, Position = 10 },

            // Server 4: Direct Backup (Dự Phòng) - 2 qualities (only 720p & 480p)
            new() { ServerName = "Direct Backup (Dự Phòng)", ServerType = StreamServerType.BackupStream, Quality = "720p HD", StreamUrl = u3, Position = 11 },
            new() { ServerName = "Direct Backup (Dự Phòng)", ServerType = StreamServerType.BackupStream, Quality = "480p SD", StreamUrl = u4, Position = 12 }
        ];
    }

    private static List<EpisodeDto> BuildEpisodes(
        int movieId,
        int baseDuration,
        IReadOnlyList<string> titles,
        params string[] thumbnails)
    {
        return titles.Select((title, index) => new EpisodeDto
        {
            Id = movieId * 100 + index + 1,
            MovieId = movieId,
            EpisodeNumber = index + 1,
            Title = $"Tập {index + 1}: {title}",
            DurationMinutes = baseDuration + index % 3 - 1,
            ThumbnailUrl = thumbnails.Length > 0 ? thumbnails[index % thumbnails.Length] : $"/images/movies/movie-{movieId}-backdrop.webp",
            Overview = $"Diễn biến tập {index + 1} của bộ phim, tiếp nối câu chuyện với những khám phá và xung đột mới.",
            Streams = GenerateMultiServerStreams(movieId * 10 + index)
        }).ToList();
    }

    public static List<GenreDto> CatalogGenres { get; } =
    [
        new() { Id = 1, Name = "Hành Động", Slug = "hanh-dong" },
        new() { Id = 2, Name = "Khoa Học Viễn Tưởng", Slug = "khoa-hoc-vien-tuong" },
        new() { Id = 3, Name = "Kỳ Ảo", Slug = "ky-ao" },
        new() { Id = 4, Name = "Phiêu Lưu", Slug = "phieu-luu" },
        new() { Id = 5, Name = "Kịch Tính", Slug = "kich-tinh" },
        new() { Id = 6, Name = "Anime", Slug = "anime" },
        new() { Id = 7, Name = "Lịch Sử", Slug = "lich-su" },
        new() { Id = 8, Name = "Tội Phạm", Slug = "toi-pham" },
        new() { Id = 9, Name = "Tâm Lý", Slug = "tam-ly" },
        new() { Id = 10, Name = "Hài Hước", Slug = "hai-huoc" },
        new() { Id = 11, Name = "Kinh Dị", Slug = "kinh-di" },
        new() { Id = 12, Name = "Lãng Mạn", Slug = "lang-man" },
        new() { Id = 13, Name = "Gia Đình", Slug = "gia-dinh" },
        new() { Id = 14, Name = "Hoạt Hình", Slug = "hoat-hinh" },
        new() { Id = 15, Name = "Chiến Tranh", Slug = "chien-tranh" }
    ];

    static DatabaseSeeder()
    {
        foreach (var genre in CatalogGenres)
        {
            genre.MovieCount = CatalogMovies.Count(m => m.Genres.Any(g => g.Equals(genre.Name, StringComparison.OrdinalIgnoreCase)));
        }

        foreach (var movie in CatalogMovies)
        {
            movie.SimilarMovies = CatalogMovies
                .Where(m => m.Id != movie.Id && (m.Type == movie.Type || m.Genres.Any(g => movie.Genres.Contains(g))))
                .Take(4)
                .Cast<MovieDto>()
                .ToList();

            foreach (var ep in movie.Episodes)
            {
                if (ep.Streams.Select(s => s.ServerName).Distinct().Count() < 4)
                {
                    ep.Streams = GenerateMultiServerStreams(ep.Id);
                }
            }
        }
    }

    public static async Task SeedCatalogAsync(AppDbContext context, CancellationToken cancellationToken = default)
    {
        var genreEntities = await context.Genres.ToListAsync(cancellationToken);
        var genresByName = genreEntities.ToDictionary(genre => genre.Name, StringComparer.OrdinalIgnoreCase);
        var usedGenreIds = genreEntities.Select(genre => genre.Id).ToHashSet();
        var nextGenreId = usedGenreIds.Count == 0 ? 1 : usedGenreIds.Max() + 1;

        foreach (var sourceGenre in CatalogGenres)
        {
            if (genresByName.ContainsKey(sourceGenre.Name)) continue;

            var genreId = usedGenreIds.Add(sourceGenre.Id) ? sourceGenre.Id : nextGenreId++;
            var genre = new GenreEntity { Id = genreId, Name = sourceGenre.Name, Slug = sourceGenre.Slug };
            genreEntities.Add(genre);
            genresByName.Add(genre.Name, genre);
            context.Genres.Add(genre);
        }

        foreach (var genreName in CatalogMovies.SelectMany(movie => movie.Genres).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (genresByName.ContainsKey(genreName)) continue;

            while (!usedGenreIds.Add(nextGenreId)) nextGenreId++;
            var genre = new GenreEntity { Id = nextGenreId, Name = genreName, Slug = $"genre-{nextGenreId}" };
            nextGenreId++;
            genreEntities.Add(genre);
            genresByName.Add(genreName, genre);
            context.Genres.Add(genre);
        }

        var existingMovieIds = await context.Movies
            .Select(movie => movie.Id)
            .ToHashSetAsync(cancellationToken);

        foreach (var source in CatalogMovies)
        {
            if (existingMovieIds.Contains(source.Id)) continue;

            var movie = new MovieEntity
            {
                Id = source.Id,
                Title = source.Title,
                OriginalTitle = source.OriginalTitle,
                Slug = source.Slug,
                Overview = source.Overview,
                PosterUrl = source.PosterUrl,
                BackdropUrl = source.BackdropUrl,
                TrailerUrl = source.TrailerUrl,
                Rating = source.Rating,
                VoteCount = source.VoteCount,
                ReleaseYear = source.ReleaseYear,
                DurationMinutes = source.DurationMinutes,
                AgeRating = source.AgeRating,
                Quality = source.Quality,
                Type = source.Type,
                Country = source.Country,
                ViewCount = source.ViewCount,
                IsTrending = source.IsTrending,
                IsFeatured = source.IsFeatured,
                Director = source.Director,
                TotalEpisodes = source.TotalEpisodes,
                CurrentEpisode = source.CurrentEpisode,
                MovieGenres = source.Genres.Select(name => new MovieGenreEntity { Genre = genresByName[name] }).ToList(),
                CastMembers = source.Cast.Select((name, index) => new CastMemberEntity { Position = index + 1, Name = name }).ToList(),
                Episodes = source.Episodes.Select(episode => new EpisodeEntity
                {
                    Id = episode.Id,
                    EpisodeNumber = episode.EpisodeNumber,
                    Title = episode.Title,
                    DurationMinutes = episode.DurationMinutes,
                    ThumbnailUrl = episode.ThumbnailUrl,
                    Overview = episode.Overview,
                    Streams = episode.Streams.Select((stream, index) => new StreamSourceEntity
                    {
                        Position = index + 1,
                        ServerName = stream.ServerName,
                        ServerType = stream.ServerType,
                        Quality = stream.Quality,
                        StreamUrl = stream.StreamUrl
                    }).ToList()
                }).ToList()
            };
            context.Movies.Add(movie);
        }

        await context.SaveChangesAsync(cancellationToken);

        // Populate the normalized search text for every seeded movie. The FTS trigger keeps the
        // index in sync through the movies.SearchText column.
        await PopulateSearchTextAsync(context, cancellationToken);

        var episodesToUpdate = await context.Episodes.Include(e => e.Streams)
            .Where(e => e.Streams.Select(s => s.ServerName).Distinct().Count() < 4)
            .ToListAsync(cancellationToken);
        if (episodesToUpdate.Count > 0)
        {
            foreach (var ep in episodesToUpdate)
            {
                context.StreamSources.RemoveRange(ep.Streams);
                var newStreams = GenerateMultiServerStreams(ep.Id);
                foreach (var s in newStreams)
                {
                    context.StreamSources.Add(new StreamSourceEntity
                    {
                        EpisodeId = ep.Id,
                        Position = s.Position,
                        ServerName = s.ServerName,
                        ServerType = s.ServerType,
                        Quality = s.Quality,
                        StreamUrl = s.StreamUrl
                    });
                }
            }
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Computes the diacritic free search text for every movie that still misses it.
    /// </summary>
    public static async Task<int> PopulateSearchTextAsync(AppDbContext context, CancellationToken cancellationToken = default)
    {
        var movies = await context.Movies
            .Include(movie => movie.CastMembers)
            .Where(movie => movie.SearchText == null || movie.SearchText == string.Empty)
            .ToListAsync(cancellationToken);

        if (movies.Count == 0) return 0;

        foreach (var movie in movies)
        {
            movie.SearchText = WebXemPhim.Shared.Utils.MovieSearchTextBuilder.Build(
                movie.Title,
                movie.OriginalTitle,
                movie.Director,
                movie.CastMembers.Select(c => c.Name),
                movie.Overview);
        }

        await context.SaveChangesAsync(cancellationToken);
        return movies.Count;
    }

    public static async Task SeedAsync(AppDbContext context, TimeProvider? timeProvider = null, CancellationToken cancellationToken = default)
    {
        var provider = timeProvider ?? TimeProvider.System;        var now = provider.GetUtcNow().UtcDateTime;

        // 1. Seed catalog
        await SeedCatalogAsync(context, cancellationToken);

        // 2. Seed default VIP plans
        if (!await context.VipPlans.AnyAsync(cancellationToken))
        {
            context.VipPlans.AddRange(
                new VipPlanEntity
                {
                    Id = "vip-30",
                    Name = "Một tháng",
                    Description = "Linh hoạt, dễ bắt đầu - 30 ngày trải nghiệm xem phim không giới hạn",
                    PriceVnd = 79000m,
                    DurationDays = 30,
                    IsActive = true,
                    DisplayOrder = 1
                },
                new VipPlanEntity
                {
                    Id = "vip-90",
                    Name = "Ba tháng",
                    Description = "Tiết kiệm hơn, trải nghiệm dài hơn - 90 ngày xem phim chất lượng cao",
                    PriceVnd = 199000m,
                    DurationDays = 90,
                    IsActive = true,
                    DisplayOrder = 2
                },
                new VipPlanEntity
                {
                    Id = "vip-180",
                    Name = "Sáu tháng",
                    Description = "Bán chạy nhất cho người luyện phim - 180 ngày đặc quyền VIP",
                    PriceVnd = 349000m,
                    DurationDays = 180,
                    IsActive = true,
                    DisplayOrder = 3
                },
                new VipPlanEntity
                {
                    Id = "vip-365",
                    Name = "Một năm",
                    Description = "Lựa chọn tốt nhất cho người yêu phim - 365 ngày đặc quyền tối thượng",
                    PriceVnd = 499000m,
                    DurationDays = 365,
                    IsActive = true,
                    DisplayOrder = 4
                }
            );
            await context.SaveChangesAsync(cancellationToken);
        }

        // 3. Seed default coupons
        if (!await context.Coupons.AnyAsync(cancellationToken))
        {
            var validUntil = now.AddYears(5);
            context.Coupons.AddRange(
                new CouponEntity
                {
                    Code = "CINEMA2026",
                    DiscountType = "percent",
                    DiscountValue = 20m,
                    ValidUntilUtc = validUntil,
                    IsActive = true
                },
                new CouponEntity
                {
                    Code = "VIPPRO",
                    DiscountType = "percent",
                    DiscountValue = 15m,
                    ValidUntilUtc = validUntil,
                    IsActive = true
                },
                new CouponEntity
                {
                    Code = "TRIAN50K",
                    DiscountType = "fixed",
                    DiscountValue = 50000m,
                    ValidUntilUtc = validUntil,
                    IsActive = true
                },
                new CouponEntity
                {
                    Code = "TESTVIP",
                    DiscountType = "percent",
                    DiscountValue = 50m,
                    ValidUntilUtc = validUntil,
                    IsActive = true
                }
            );
            await context.SaveChangesAsync(cancellationToken);
        }

        // 4. Seed demo accounts, payments, and reviews if Users table is empty
        if (!await context.Users.AnyAsync(cancellationToken))
        {
            var (saltAdmin, hashAdmin) = HashPassword("Password123!");
            var adminUser = new UserEntity
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                FullName = "Quản Trị Viên",
                Email = "admin@cinemaflow.vn",
                NormalizedEmail = "admin@cinemaflow.vn",
                EmailLookupHash = FieldEncryptor.Current.ComputeLookupHash("admin@cinemaflow.vn"),
                PasswordSalt = saltAdmin,
                PasswordHash = hashAdmin,
                Membership = MembershipTier.Vip,
                VipExpiresAtUtc = now.AddYears(1),
                CreatedAtUtc = now.AddMonths(-6)
            };

            var (saltVip1, hashVip1) = HashPassword("Password123!");
            var vipUser1 = new UserEntity
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                FullName = "Trần Hoàng Nam",
                Email = "hoangnam@cinemaflow.vn",
                NormalizedEmail = "hoangnam@cinemaflow.vn",
                EmailLookupHash = FieldEncryptor.Current.ComputeLookupHash("hoangnam@cinemaflow.vn"),
                PasswordSalt = saltVip1,
                PasswordHash = hashVip1,
                Membership = MembershipTier.Vip,
                VipExpiresAtUtc = now.AddDays(90),
                CreatedAtUtc = now.AddMonths(-3)
            };

            var (saltVip2, hashVip2) = HashPassword("Password123!");
            var vipUser2 = new UserEntity
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                FullName = "VIP Member",
                Email = "vip@cinemaflow.vn",
                NormalizedEmail = "vip@cinemaflow.vn",
                EmailLookupHash = FieldEncryptor.Current.ComputeLookupHash("vip@cinemaflow.vn"),
                PasswordSalt = saltVip2,
                PasswordHash = hashVip2,
                Membership = MembershipTier.Vip,
                VipExpiresAtUtc = now.AddDays(30),
                CreatedAtUtc = now.AddDays(-15)
            };

            var (saltUser1, hashUser1) = HashPassword("Password123!");
            var regularUser1 = new UserEntity
            {
                Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                FullName = "Nguyễn Thùy Linh",
                Email = "thuylinh@cinemaflow.vn",
                NormalizedEmail = "thuylinh@cinemaflow.vn",
                EmailLookupHash = FieldEncryptor.Current.ComputeLookupHash("thuylinh@cinemaflow.vn"),
                PasswordSalt = saltUser1,
                PasswordHash = hashUser1,
                Membership = MembershipTier.Free,
                CreatedAtUtc = now.AddMonths(-1)
            };

            var (saltUser2, hashUser2) = HashPassword("Password123!");
            var regularUser2 = new UserEntity
            {
                Id = Guid.Parse("55555555-5555-5555-5555-555555555555"),
                FullName = "Lê Bảo Long",
                Email = "longle@cinemaflow.vn",
                NormalizedEmail = "longle@cinemaflow.vn",
                EmailLookupHash = FieldEncryptor.Current.ComputeLookupHash("longle@cinemaflow.vn"),
                PasswordSalt = saltUser2,
                PasswordHash = hashUser2,
                Membership = MembershipTier.Free,
                CreatedAtUtc = now.AddDays(-20)
            };

            context.Users.AddRange(adminUser, vipUser1, vipUser2, regularUser1, regularUser2);
            context.UserSecurity.Add(new UserSecurityEntity { UserId = adminUser.Id, IsAdmin = true });

            // Demo payments
            context.Payments.AddRange(
                new PaymentEntity
                {
                    Id = Guid.NewGuid(),
                    OrderCode = "CF20260101VIP365ADMIN",
                    UserId = adminUser.Id,
                    PlanId = "vip-365",
                    PlanName = "VIP 365 ngày",
                    Amount = 499000m,
                    Method = PaymentMethod.BankTransfer,
                    Status = PaymentStatus.Completed,
                    CreatedAtUtc = now.AddMonths(-6),
                    PaidAtUtc = now.AddMonths(-6),
                    VipExpiresAtUtc = adminUser.VipExpiresAtUtc
                },
                new PaymentEntity
                {
                    Id = Guid.NewGuid(),
                    OrderCode = "CF20260215VIP90NAM",
                    UserId = vipUser1.Id,
                    PlanId = "vip-90",
                    PlanName = "VIP 90 ngày",
                    Amount = 149000m,
                    Method = PaymentMethod.Momo,
                    Status = PaymentStatus.Completed,
                    CreatedAtUtc = now.AddDays(-3),
                    PaidAtUtc = now.AddDays(-3),
                    VipExpiresAtUtc = vipUser1.VipExpiresAtUtc
                },
                new PaymentEntity
                {
                    Id = Guid.NewGuid(),
                    OrderCode = "CF20260301VIP30MEMBER",
                    UserId = vipUser2.Id,
                    PlanId = "vip-30",
                    PlanName = "VIP 30 ngày",
                    Amount = 59000m,
                    Method = PaymentMethod.Visa,
                    Status = PaymentStatus.Completed,
                    CreatedAtUtc = now.AddDays(-15),
                    PaidAtUtc = now.AddDays(-15),
                    VipExpiresAtUtc = vipUser2.VipExpiresAtUtc
                },
                new PaymentEntity
                {
                    Id = Guid.NewGuid(),
                    OrderCode = "CF20260320VIP180PENDING",
                    UserId = vipUser1.Id,
                    PlanId = "vip-180",
                    PlanName = "VIP 6 Tháng",
                    Amount = 349000m,
                    Method = PaymentMethod.BankTransfer,
                    Status = PaymentStatus.Pending,
                    CreatedAtUtc = now.AddHours(-2),
                    TransactionReference = "FT2609081239MB",
                    Note = "Chuyển khoản VietQR chờ đối soát sao kê ngân hàng"
                },
                new PaymentEntity
                {
                    Id = Guid.NewGuid(),
                    OrderCode = "CF20260318VIP30FAILED",
                    UserId = vipUser2.Id,
                    PlanId = "vip-30",
                    PlanName = "VIP 1 Tháng",
                    Amount = 79000m,
                    Method = PaymentMethod.Momo,
                    Status = PaymentStatus.Failed,
                    CreatedAtUtc = now.AddDays(-1),
                    Note = "Từ chối duyệt: Giao dịch MoMo bị hủy hoặc quá hạn thanh toán"
                }
            );

            // Demo reviews
            context.Reviews.AddRange(
                new ReviewEntity
                {
                    Id = Guid.NewGuid(),
                    MovieId = 1,
                    UserId = adminUser.Id,
                    Rating = 10,
                    Comment = "Đỉnh cao điện ảnh khoa học viễn tưởng! Denis Villeneuve đã khép lại thiên anh hùng ca Xứ Cát bằng một kiệt tác hoàn hảo từ âm thanh, kỹ xảo đến chiều sâu tư tưởng.",
                    CreatedAtUtc = now.AddDays(-10),
                    UpdatedAtUtc = now.AddDays(-10)
                },
                new ReviewEntity
                {
                    Id = Guid.NewGuid(),
                    MovieId = 1,
                    UserId = vipUser1.Id,
                    Rating = 9,
                    Comment = "Cảnh đại chiến cát sa mạc quá choáng ngợp. Âm nhạc của Hans Zimmer rung chuyển cả phòng chiếu, xứng đáng xem bản 4K HDR.",
                    CreatedAtUtc = now.AddDays(-8),
                    UpdatedAtUtc = now.AddDays(-8)
                },
                new ReviewEntity
                {
                    Id = Guid.NewGuid(),
                    MovieId = 1,
                    UserId = regularUser1.Id,
                    Rating = 9,
                    Comment = "Diễn xuất của Timothée Chalamet và Zendaya ngày càng chín muồi. Một cái kết đầy ám ảnh.",
                    CreatedAtUtc = now.AddDays(-5),
                    UpdatedAtUtc = now.AddDays(-5)
                },
                new ReviewEntity
                {
                    Id = Guid.NewGuid(),
                    MovieId = 2,
                    UserId = vipUser1.Id,
                    Rating = 10,
                    Comment = "Cillian Murphy xứng đáng 10/10 với ánh mắt đầy dằn vặt. Đoạn thử nghiệm Trinity khiến cả phòng nín thở.",
                    CreatedAtUtc = now.AddDays(-12),
                    UpdatedAtUtc = now.AddDays(-12)
                },
                new ReviewEntity
                {
                    Id = Guid.NewGuid(),
                    MovieId = 2,
                    UserId = regularUser2.Id,
                    Rating = 9,
                    Comment = "Nolan tiếp tục chứng minh khả năng kể chuyện phi tuyến tính bậc thầy. Thoại và âm nhạc phối hợp không một vết gợn.",
                    CreatedAtUtc = now.AddDays(-9),
                    UpdatedAtUtc = now.AddDays(-9)
                },
                new ReviewEntity
                {
                    Id = Guid.NewGuid(),
                    MovieId = 3,
                    UserId = regularUser1.Id,
                    Rating = 10,
                    Comment = "Mùa 2 vượt ngoài mọi kỳ vọng! Phần hoạt họa của Fortiche đẹp như tranh vẽ, âm nhạc quá cảm xúc.",
                    CreatedAtUtc = now.AddDays(-4),
                    UpdatedAtUtc = now.AddDays(-4)
                },
                new ReviewEntity
                {
                    Id = Guid.NewGuid(),
                    MovieId = 3,
                    UserId = vipUser2.Id,
                    Rating = 9,
                    Comment = "Mối quan hệ giữa Vi và Jinx đau lòng đến nghẹn thở. Phim hoạt hình xuất sắc nhất thập kỷ.",
                    CreatedAtUtc = now.AddDays(-2),
                    UpdatedAtUtc = now.AddDays(-2)
                },
                new ReviewEntity
                {
                    Id = Guid.NewGuid(),
                    MovieId = 4,
                    UserId = adminUser.Id,
                    Rating = 10,
                    Comment = "Heath Ledger với vai Joker là huyền thoại sống mãi trong lịch sử điện ảnh thế giới.",
                    CreatedAtUtc = now.AddDays(-20),
                    UpdatedAtUtc = now.AddDays(-20)
                },
                new ReviewEntity
                {
                    Id = Guid.NewGuid(),
                    MovieId = 5,
                    UserId = regularUser2.Id,
                    Rating = 9,
                    Comment = "Phần 2 hành động mãn nhãn, đội quân bóng đêm trỗi dậy ngầu đét! Đồ họa của A-1 Pictures rất mượt.",
                    CreatedAtUtc = now.AddDays(-3),
                    UpdatedAtUtc = now.AddDays(-3)
                },
                new ReviewEntity
                {
                    Id = Guid.NewGuid(),
                    MovieId = 6,
                    UserId = vipUser1.Id,
                    Rating = 9,
                    Comment = "Tái hiện văn hóa võ sĩ đạo và mưu lược chính trị thời phong kiến Nhật Bản chuẩn chỉ đến từng chi tiết nhỏ.",
                    CreatedAtUtc = now.AddDays(-15),
                    UpdatedAtUtc = now.AddDays(-15)
                },
                new ReviewEntity
                {
                    Id = Guid.NewGuid(),
                    MovieId = 11,
                    UserId = regularUser1.Id,
                    Rating = 10,
                    Comment = "Kịch bản xuất sắc không một hạt sạn. Phản ánh chân thực sự phân hóa giàu nghèo sâu sắc.",
                    CreatedAtUtc = now.AddDays(-25),
                    UpdatedAtUtc = now.AddDays(-25)
                },
                new ReviewEntity
                {
                    Id = Guid.NewGuid(),
                    MovieId = 13,
                    UserId = vipUser2.Id,
                    Rating = 8,
                    Comment = "Phương Anh Đào diễn xuất rất có hồn, ánh mắt chất chứa nỗi niềm. Góc quay và ánh sáng tiến bộ rõ rệt.",
                    CreatedAtUtc = now.AddDays(-6),
                    UpdatedAtUtc = now.AddDays(-6)
                },
                new ReviewEntity
                {
                    Id = Guid.NewGuid(),
                    MovieId = 13,
                    UserId = regularUser1.Id,
                    Rating = 9,
                    Comment = "Một bộ phim chạm đến cảm xúc của nhiều người phụ nữ hiện đại. Nhạc phim của Phan Mạnh Quỳnh rất hay.",
                    CreatedAtUtc = now.AddDays(-4),
                    UpdatedAtUtc = now.AddDays(-4)
                },
                new ReviewEntity
                {
                    Id = Guid.NewGuid(),
                    MovieId = 14,
                    UserId = vipUser1.Id,
                    Rating = 9,
                    Comment = "Lấy đi rất nhiều nước mắt. Cảnh bà Hai ngồi chờ các con nơi quê nhà làm ai xa quê cũng phải quặn lòng.",
                    CreatedAtUtc = now.AddDays(-7),
                    UpdatedAtUtc = now.AddDays(-7)
                },
                new ReviewEntity
                {
                    Id = Guid.NewGuid(),
                    MovieId = 17,
                    UserId = adminUser.Id,
                    Rating = 9,
                    Comment = "Không khí kinh dị tâm linh rùng rợn, lồng ghép lịch sử tinh tế. Choi Min-sik và Kim Go-eun phối hợp ăn ý.",
                    CreatedAtUtc = now.AddDays(-11),
                    UpdatedAtUtc = now.AddDays(-11)
                }
            );

            // Seed user settings for admin
            context.UserSettings.Add(new UserSettingsEntity
            {
                UserId = adminUser.Id,
                DefaultQuality = "4K khi có thể",
                AutoplayNext = true,
                NotifyNewEpisodes = true,
                Language = "vi",
                UpdatedAtUtc = now
            });

            // Seed notifications
            context.Notifications.AddRange(
                new NotificationEntity
                {
                    Id = Guid.NewGuid(),
                    UserId = adminUser.Id,
                    Title = "Tập mới đã lên sóng",
                    Copy = "Xứ Cát: Phần Ba vừa cập nhật bản chiếu rạp đầy đủ 4K HDR.",
                    Icon = "Play",
                    ActionUrl = "/watch/1",
                    Read = false,
                    CreatedAtUtc = now.AddMinutes(-12)
                },
                new NotificationEntity
                {
                    Id = Guid.NewGuid(),
                    UserId = adminUser.Id,
                    Title = "CinemaFlow VIP",
                    Copy = "Máy chủ VIP 4K mới đã sẵn sàng cho tài khoản của bạn.",
                    Icon = "Crown",
                    ActionUrl = "/vip",
                    Read = true,
                    CreatedAtUtc = now.AddDays(-1)
                }
            );

            // Seed watch progress
            context.WatchProgress.AddRange(
                new WatchProgressEntity
                {
                    UserId = adminUser.Id,
                    MovieId = 1,
                    EpisodeNumber = 1,
                    ProgressSeconds = 2400,
                    DurationSeconds = 10080,
                    LastWatchedAtUtc = now.AddHours(-1)
                },
                new WatchProgressEntity
                {
                    UserId = adminUser.Id,
                    MovieId = 2,
                    EpisodeNumber = 1,
                    ProgressSeconds = 3100,
                    DurationSeconds = 10800,
                    LastWatchedAtUtc = now.AddHours(-5)
                }
            );

            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private static (byte[] Salt, byte[] Hash) HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            100_000,
            HashAlgorithmName.SHA256,
            32);
        return (salt, hash);
    }
}