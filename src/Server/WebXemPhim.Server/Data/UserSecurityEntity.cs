using WebXemPhim.Server.Security;

namespace WebXemPhim.Server.Data;

public sealed class UserSecurityEntity
{
    public Guid UserId { get; set; }
    public UserEntity User { get; set; } = null!;
    public bool IsAdmin { get; set; }
    public int PasswordIterations { get; set; } = CredentialSecurity.LegacyIterations;
    public int FailedLoginAttempts { get; set; }
    public DateTime? LockedUntilUtc { get; set; }
    public long Version { get; set; }
}
