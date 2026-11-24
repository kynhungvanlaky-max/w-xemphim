using WebXemPhim.Server.Security;

namespace WebXemPhim.Server.Data;

public sealed class UserSecurityEntity
{
    public Guid UserId { get; set; }
    public UserEntity User { get; set; } = null!;
    public bool IsAdmin { get; set; }
    public int PasswordIterations { get; set; } = CredentialSecurity.LegacyIterations;
    public int FailedLoginAttempts { get; set; }
    /// <summary>Khóa đình chỉ bởi quản trị viên — vô hiệu hóa cả đăng nhập mới lẫn phiên Bearer đang hoạt động.</summary>
    public DateTime? LockedUntilUtc { get; set; }
    /// <summary>Tạm dừng đăng nhập 15 phút sau 5 lần sai mật khẩu/MFA — chỉ chặn đăng nhập mới, không hủy phiên đang hoạt động.</summary>
    public DateTime? LoginCooldownUntilUtc { get; set; }
    public long Version { get; set; }
}
