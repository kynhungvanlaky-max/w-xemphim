namespace WebXemPhim.Shared.Enums;

public enum MovieType
{
    Movie = 1,      // Phim Lẻ
    Series = 2,     // Phim Bộ
    Anime = 3       // Phim Hoạt Hình / Anime
}

public enum VideoQuality
{
    HD_720p = 1,
    FHD_1080p = 2,
    UHD_4K = 3
}

public enum StreamServerType
{
    VipFastServer = 1,
    HydraCdn = 2,
    BackupStream = 3
}
