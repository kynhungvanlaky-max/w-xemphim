using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Security;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Security;

namespace WebXemPhim.Server.Services;

public enum AuthFailure
{
    None,
    DuplicateEmail,
    InvalidCredentials,
    UserNotFound,
    InvalidResetToken,
    WeakPassword
}

public record AuthResult(AuthDto? Data, AuthFailure Failure = AuthFailure.None);
public record PasswordResetResult(PasswordResetDto? Data, AuthFailure Failure = AuthFailure.None);

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(RegisterRequest request);
    Task<AuthResult> LoginAsync(LoginRequest request);
    Task<AuthResult> VerifyMfaLoginAsync(VerifyMfaLoginRequest request);
    Task<PasswordResetResult> CreatePasswordResetAsync(string email);
    Task<bool> ResetPasswordAsync(ResetPasswordRequest request);
    Task<UserDto?> ValidateAccessTokenAsync(string token);
    Task RevokeAccessTokenAsync(string token);
    Task<UserDto?> GetUserAsync(Guid userId);
    Task<UserDto?> ActivateVipAsync(Guid userId, int durationDays);
    Task<UserSettingsDto?> GetSettingsAsync(Guid userId);
    Task<bool> UpdateSettingsAsync(Guid userId, UserSettingsDto settings);
    Task<MfaStatusDto> GetMfaStatusAsync(Guid userId);
    Task<MfaSetupDto?> BeginMfaSetupAsync(Guid userId, string currentPassword);
    Task<MfaRecoveryCodesDto?> EnableMfaAsync(Guid userId, string code, string currentAccessToken);
    Task<bool> DisableMfaAsync(Guid userId, string currentPassword, string code);
    Task<bool> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, string currentAccessToken = "");
}

public sealed class AuthService(
    IDbContextFactory<AppDbContext> contextFactory,
    TimeProvider timeProvider,
    IDataProtectionProvider dataProtectionProvider) : IAuthService
{
    private readonly IDataProtector mfaProtector = dataProtectionProvider.CreateProtector("CinemaFlow.MfaSecret.v1");

    public AuthService(IDbContextFactory<AppDbContext> contextFactory, TimeProvider timeProvider)
        : this(contextFactory, timeProvider, new EphemeralDataProtectionProvider()) { }
    public async Task<AuthResult> RegisterAsync(RegisterRequest request)
    {
        if (PasswordPolicyValidator.IsCommonOrWeak(request.Password))
        {
            return new AuthResult(null, AuthFailure.WeakPassword);
        }

        var email = NormalizeEmail(request.Email);
        await using var context = await contextFactory.CreateDbContextAsync();
        if (await context.Users.AnyAsync(user => user.NormalizedEmail == email))
        {
            return new AuthResult(null, AuthFailure.DuplicateEmail);
        }

        var salt = RandomNumberGenerator.GetBytes(16);
        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            FullName = request.FullName.Trim(),
            Email = request.Email.Trim(),
            NormalizedEmail = email,
            PasswordSalt = salt,
            PasswordHash = CredentialSecurity.PasswordHash(request.Password, salt),
            Membership = MembershipTier.Free,
            CreatedAtUtc = UtcNow
        };
        context.Users.Add(user);
        context.UserSecurity.Add(new UserSecurityEntity { UserId = user.Id, PasswordIterations = CredentialSecurity.PasswordIterations });

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            if (!await context.Users.AsNoTracking().AnyAsync(item => item.NormalizedEmail == email)) throw;
            return new AuthResult(null, AuthFailure.DuplicateEmail);
        }

        return new AuthResult(await CreateSessionAsync(context, user));
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request)
    {
        // Optimistic concurrency keeps parallel failed attempts from overwriting each other.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try { return await TryLoginAsync(request); }
            catch (DbUpdateConcurrencyException) { }
        }
        return new AuthResult(null, AuthFailure.InvalidCredentials);
    }

    private async Task<AuthResult> TryLoginAsync(LoginRequest request)
    {
        var email = NormalizeEmail(request.Email);
        await using var context = await contextFactory.CreateDbContextAsync();
        var user = await context.Users.SingleOrDefaultAsync(candidate => candidate.NormalizedEmail == email);
        if (user is null)
        {
            CredentialSecurity.PasswordHash(request.Password, new byte[16]);
            return new AuthResult(null, AuthFailure.InvalidCredentials);
        }

        var security = await context.UserSecurity.SingleOrDefaultAsync(item => item.UserId == user.Id);
        if (security is null) { security = new UserSecurityEntity { UserId = user.Id }; context.UserSecurity.Add(security); }
        if (security.LockedUntilUtc > UtcNow) return new AuthResult(null, AuthFailure.InvalidCredentials);
        if (security.LockedUntilUtc is not null) { security.LockedUntilUtc = null; security.FailedLoginAttempts = 0; }
        var valid = CredentialSecurity.Verify(request.Password, user.PasswordSalt, user.PasswordHash, security.PasswordIterations);
        security.Version++;

        if (!valid)
        {
            if (++security.FailedLoginAttempts >= 5) security.LockedUntilUtc = UtcNow.AddMinutes(15);
            await context.SaveChangesAsync();
            return new AuthResult(null, AuthFailure.InvalidCredentials);
        }
        security.FailedLoginAttempts = 0;
        if (security.PasswordIterations < CredentialSecurity.PasswordIterations)
        {
            user.PasswordSalt = RandomNumberGenerator.GetBytes(16);
            user.PasswordHash = CredentialSecurity.PasswordHash(request.Password, user.PasswordSalt);
            security.PasswordIterations = CredentialSecurity.PasswordIterations;
        }
        var mfa = await context.MfaCredentials.SingleOrDefaultAsync(item => item.UserId == user.Id && item.Enabled);
        if (mfa is not null)
        {
            var challengeToken = CredentialSecurity.CreateToken();
            context.MfaChallenges.RemoveRange(await context.MfaChallenges.Where(item => item.UserId == user.Id).ToListAsync());
            context.MfaChallenges.Add(new MfaChallengeEntity
            {
                TokenHash = CredentialSecurity.TokenHash(challengeToken), UserId = user.Id, ExpiresAtUtc = UtcNow.AddMinutes(5)
            });
            await context.SaveChangesAsync();
            return new AuthResult(new AuthDto
            {
                RequiresMfa = true, MfaChallengeToken = challengeToken, ExpiresAt = UtcNow.AddMinutes(5)
            });
        }
        return new AuthResult(await CreateSessionAsync(context, user));
    }

    public async Task<AuthResult> VerifyMfaLoginAsync(VerifyMfaLoginRequest request)
    {
        if (!CredentialSecurity.IsToken(request.ChallengeToken)) return new AuthResult(null, AuthFailure.InvalidCredentials);
        var tokenHash = CredentialSecurity.TokenHash(request.ChallengeToken);
        await using var context = await contextFactory.CreateDbContextAsync();
        var challenge = await context.MfaChallenges.Include(item => item.User).SingleOrDefaultAsync(item => item.TokenHash == tokenHash);
        if (challenge is null || challenge.ExpiresAtUtc <= UtcNow || challenge.FailedAttempts >= 5)
        {
            if (challenge is not null) { context.MfaChallenges.Remove(challenge); await context.SaveChangesAsync(); }
            return new AuthResult(null, AuthFailure.InvalidCredentials);
        }
        var credential = await context.MfaCredentials.SingleOrDefaultAsync(item => item.UserId == challenge.UserId && item.Enabled);
        if (credential is null) return new AuthResult(null, AuthFailure.InvalidCredentials);

        var valid = VerifyMfaCode(credential, request.Code, consumeRecoveryCode: true);
        if (!valid)
        {
            challenge.FailedAttempts++;
            if (challenge.FailedAttempts >= 5) context.MfaChallenges.Remove(challenge);
            await context.SaveChangesAsync();
            return new AuthResult(null, AuthFailure.InvalidCredentials);
        }

        context.MfaChallenges.RemoveRange(await context.MfaChallenges.Where(item => item.UserId == challenge.UserId).ToListAsync());
        await context.SaveChangesAsync();
        return new AuthResult(await CreateSessionAsync(context, challenge.User, mfaVerified: true));
    }

    public async Task<PasswordResetResult> CreatePasswordResetAsync(string email)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var normalizedEmail = NormalizeEmail(email);
        var user = await context.Users.SingleOrDefaultAsync(candidate => candidate.NormalizedEmail == normalizedEmail);
        if (user is null)
        {
            return new PasswordResetResult(null, AuthFailure.UserNotFound);
        }

        var token = CredentialSecurity.CreateToken();
        var reset = new PasswordResetTokenEntity
        {
            Token = CredentialSecurity.TokenHash(token),
            UserId = user.Id,
            ExpiresAtUtc = UtcNow.AddMinutes(15)
        };
        context.PasswordResetTokens.Add(reset);
        await context.SaveChangesAsync();
        return new PasswordResetResult(new PasswordResetDto
        {
            ResetToken = token,
            ExpiresAt = reset.ExpiresAtUtc
        });
    }

    public async Task<bool> ResetPasswordAsync(ResetPasswordRequest request)
    {
        if (!CredentialSecurity.IsToken(request.Token)) return false;
        if (PasswordPolicyValidator.IsCommonOrWeak(request.NewPassword)) return false;
        var tokenHash = CredentialSecurity.TokenHash(request.Token);
        await using var context = await contextFactory.CreateDbContextAsync();
        var reset = await context.PasswordResetTokens
            .Include(item => item.User)
            .SingleOrDefaultAsync(item => item.Token == tokenHash);
        if (reset is null || reset.UsedAtUtc is not null || reset.ExpiresAtUtc <= UtcNow)
        {
            return false;
        }

        var salt = RandomNumberGenerator.GetBytes(16);
        reset.User.PasswordSalt = salt;
        reset.User.PasswordHash = CredentialSecurity.PasswordHash(request.NewPassword, salt);
        reset.UsedAtUtc = UtcNow;
        var security = await context.UserSecurity.SingleOrDefaultAsync(item => item.UserId == reset.UserId);
        if (security is null) { security = new UserSecurityEntity { UserId = reset.UserId }; context.UserSecurity.Add(security); }
        security.PasswordIterations = CredentialSecurity.PasswordIterations;
        security.FailedLoginAttempts = 0;
        security.LockedUntilUtc = null;
        security.Version++;
        var otherResets = await context.PasswordResetTokens.Where(item => item.UserId == reset.UserId && item.Token != tokenHash && item.UsedAtUtc == null).ToListAsync();
        foreach (var item in otherResets) item.UsedAtUtc = UtcNow;

        var accessTokens = await context.AccessTokens
            .Where(token => token.UserId == reset.UserId)
            .ToListAsync();
        context.AccessTokens.RemoveRange(accessTokens);
        context.MfaVerifiedSessions.RemoveRange(await context.MfaVerifiedSessions.Where(item => item.UserId == reset.UserId).ToListAsync());
        try { await context.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { return false; }
        return true;
    }

    public async Task<bool> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, string currentAccessToken = "")
    {
        if (string.IsNullOrWhiteSpace(request.CurrentPassword) ||
            string.IsNullOrWhiteSpace(request.NewPassword) ||
            request.NewPassword == request.CurrentPassword ||
            request.NewPassword.Length < 8 ||
            PasswordPolicyValidator.IsCommonOrWeak(request.NewPassword))
        {
            return false;
        }

        await using var context = await contextFactory.CreateDbContextAsync();
        var user = await context.Users.SingleOrDefaultAsync(item => item.Id == userId);
        if (user is null) return false;

        var security = await context.UserSecurity.SingleOrDefaultAsync(item => item.UserId == userId);
        if (security is null) { security = new UserSecurityEntity { UserId = userId }; context.UserSecurity.Add(security); }
        if (security.LockedUntilUtc > UtcNow) return false;

        var valid = CredentialSecurity.Verify(request.CurrentPassword, user.PasswordSalt, user.PasswordHash, security.PasswordIterations);
        if (!valid)
        {
            security.FailedLoginAttempts++;
            if (security.FailedLoginAttempts >= 5)
            {
                security.LockedUntilUtc = UtcNow.AddMinutes(15);
            }
            await context.SaveChangesAsync();
            return false;
        }

        var salt = RandomNumberGenerator.GetBytes(16);
        user.PasswordSalt = salt;
        user.PasswordHash = CredentialSecurity.PasswordHash(request.NewPassword, salt);
        security.PasswordIterations = CredentialSecurity.PasswordIterations;
        security.FailedLoginAttempts = 0;
        security.LockedUntilUtc = null;
        security.Version++;

        // Revoke all other access tokens except the current one (if provided)
        string? currentTokenHash = CredentialSecurity.IsToken(currentAccessToken) ? CredentialSecurity.TokenHash(currentAccessToken) : null;
        var otherTokens = await context.AccessTokens
            .Where(token => token.UserId == userId && (currentTokenHash == null || token.Token != currentTokenHash))
            .ToListAsync();
        context.AccessTokens.RemoveRange(otherTokens);

        // Invalidate unused password reset tokens
        var staleResets = await context.PasswordResetTokens.Where(t => t.UserId == userId && t.UsedAtUtc == null).ToListAsync();
        foreach (var r in staleResets) r.UsedAtUtc = UtcNow;

        try
        {
            await context.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    public async Task<UserDto?> ValidateAccessTokenAsync(string token)
    {
        if (!CredentialSecurity.IsToken(token)) return null;
        var tokenHash = CredentialSecurity.TokenHash(token);
        await using var context = await contextFactory.CreateDbContextAsync();
        var access = await context.AccessTokens
            .Include(item => item.User)
            .SingleOrDefaultAsync(item => item.Token == tokenHash);
        if (access is null)
        {
            return null;
        }

        if (access.ExpiresAtUtc <= UtcNow)
        {
            context.AccessTokens.Remove(access);
            var verified = await context.MfaVerifiedSessions.SingleOrDefaultAsync(item => item.TokenHash == tokenHash);
            if (verified is not null) context.MfaVerifiedSessions.Remove(verified);
            await context.SaveChangesAsync();
            return null;
        }

        var security = await context.UserSecurity.AsNoTracking().FirstOrDefaultAsync(item => item.UserId == access.UserId);
        if (security?.LockedUntilUtc > UtcNow)
        {
            return null;
        }

        var user = await MapUserAsync(context, access.User);
        if (user is not null)
            user.MfaSessionVerified = !user.MfaEnabled || await context.MfaVerifiedSessions.AsNoTracking().AnyAsync(item => item.TokenHash == tokenHash && item.UserId == user.Id);
        return user;
    }

    public async Task RevokeAccessTokenAsync(string token)
    {
        if (!CredentialSecurity.IsToken(token)) return;
        var tokenHash = CredentialSecurity.TokenHash(token);
        await using var context = await contextFactory.CreateDbContextAsync();
        var access = await context.AccessTokens.SingleOrDefaultAsync(item => item.Token == tokenHash);
        if (access is not null)
        {
            context.AccessTokens.Remove(access);
            var verified = await context.MfaVerifiedSessions.SingleOrDefaultAsync(item => item.TokenHash == tokenHash);
            if (verified is not null) context.MfaVerifiedSessions.Remove(verified);
            await context.SaveChangesAsync();
        }
    }

    public async Task<UserDto?> GetUserAsync(Guid userId)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        return await MapUserAsync(context, await context.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == userId));
    }

    public async Task<UserDto?> ActivateVipAsync(Guid userId, int durationDays)
    {
        if (durationDays <= 0) return null;

        await using var context = await contextFactory.CreateDbContextAsync();
        var user = await context.Users.SingleOrDefaultAsync(candidate => candidate.Id == userId);
        if (user is null) return null;

        var startsAt = user.VipExpiresAtUtc > UtcNow ? user.VipExpiresAtUtc.Value : UtcNow;
        user.Membership = MembershipTier.Vip;
        user.VipExpiresAtUtc = startsAt.AddDays(durationDays);
        await context.SaveChangesAsync();
        return await MapUserAsync(context, user);
    }

    private async Task<AuthDto> CreateSessionAsync(AppDbContext context, UserEntity user, bool mfaVerified = false)
    {
        var token = CredentialSecurity.CreateToken();
        var access = new AccessTokenEntity
        {
            Token = CredentialSecurity.TokenHash(token),
            UserId = user.Id,
            ExpiresAtUtc = UtcNow.AddDays(7)
        };
        context.AccessTokens.Add(access);
        if (mfaVerified)
            context.MfaVerifiedSessions.Add(new MfaVerifiedSessionEntity { TokenHash = access.Token, UserId = user.Id, VerifiedAtUtc = UtcNow });
        await context.SaveChangesAsync();
        var dto = new AuthDto
        {
            AccessToken = token,
            ExpiresAt = access.ExpiresAtUtc,
            User = (await MapUserAsync(context, user))!
        };
        dto.User.MfaSessionVerified = mfaVerified || !dto.User.MfaEnabled;
        return dto;
    }

    private async Task<UserDto?> MapUserAsync(AppDbContext context, UserEntity? user)
    {
        if (user is null) return null;
        var membership = user.Membership == MembershipTier.Vip && user.VipExpiresAtUtc <= UtcNow
            ? MembershipTier.Free
            : user.Membership;
        return new UserDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            Membership = membership,
            VipExpiresAt = user.VipExpiresAtUtc,
            Initials = GetInitials(user.FullName),
            IsAdmin = await context.UserSecurity.AsNoTracking().AnyAsync(item => item.UserId == user.Id && item.IsAdmin),
            MfaEnabled = await context.MfaCredentials.AsNoTracking().AnyAsync(item => item.UserId == user.Id && item.Enabled)
        };
    }

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;
    private static string NormalizeEmail(string email)
    {
        var trimmed = email.Trim().ToLowerInvariant();
        if (trimmed == "admin") return "admin@cinemaflow.vn";
        if (trimmed == "demo") return "demo@cinemaflow.vn";
        return trimmed;
    }
    private static string GetInitials(string fullName)
    {
        var words = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Concat(words.TakeLast(2).Select(word => char.ToUpperInvariant(word[0])));
    }

    public async Task<UserSettingsDto?> GetSettingsAsync(Guid userId)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var user = await context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return null;

        var settings = await context.UserSettings.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId);
        return new UserSettingsDto
        {
            DefaultQuality = settings?.DefaultQuality ?? "4K khi có thể",
            AutoplayNext = settings?.AutoplayNext ?? true,
            NotifyNewEpisodes = settings?.NotifyNewEpisodes ?? true,
            FullName = user.FullName
        };
    }

    public async Task<bool> UpdateSettingsAsync(Guid userId, UserSettingsDto settings)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var user = await context.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return false;

        if (!string.IsNullOrWhiteSpace(settings.FullName))
        {
            user.FullName = settings.FullName.Trim();
        }

        var current = await context.UserSettings.FirstOrDefaultAsync(s => s.UserId == userId);
        if (current is null)
        {
            context.UserSettings.Add(new UserSettingsEntity
            {
                UserId = userId,
                DefaultQuality = string.IsNullOrWhiteSpace(settings.DefaultQuality) ? "4K khi có thể" : settings.DefaultQuality,
                AutoplayNext = settings.AutoplayNext,
                NotifyNewEpisodes = settings.NotifyNewEpisodes,
                UpdatedAtUtc = UtcNow
            });
        }
        else
        {
            current.DefaultQuality = string.IsNullOrWhiteSpace(settings.DefaultQuality) ? "4K khi có thể" : settings.DefaultQuality;
            current.AutoplayNext = settings.AutoplayNext;
            current.NotifyNewEpisodes = settings.NotifyNewEpisodes;
            current.UpdatedAtUtc = UtcNow;
        }

        await context.SaveChangesAsync();
        return true;
    }

    public async Task<MfaStatusDto> GetMfaStatusAsync(Guid userId)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var credential = await context.MfaCredentials.AsNoTracking().SingleOrDefaultAsync(item => item.UserId == userId);
        return new MfaStatusDto { Enabled = credential?.Enabled == true, SetupPending = credential is { Enabled: false } };
    }

    public async Task<MfaSetupDto?> BeginMfaSetupAsync(Guid userId, string currentPassword)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var user = await context.Users.SingleOrDefaultAsync(item => item.Id == userId);
        var security = await context.UserSecurity.AsNoTracking().SingleOrDefaultAsync(item => item.UserId == userId);
        if (user is null || security is null || !CredentialSecurity.Verify(currentPassword, user.PasswordSalt, user.PasswordHash, security.PasswordIterations)) return null;
        var existing = await context.MfaCredentials.SingleOrDefaultAsync(item => item.UserId == userId);
        if (existing?.Enabled == true) return null;

        var secret = TotpSecurity.CreateSecret();
        var protectedSecret = mfaProtector.Protect(Convert.ToBase64String(secret));
        if (existing is null)
        {
            existing = new MfaCredentialEntity { UserId = userId };
            context.MfaCredentials.Add(existing);
        }
        existing.ProtectedSecret = protectedSecret;
        existing.RecoveryCodeHashes = string.Empty;
        existing.Enabled = false;
        existing.EnabledAtUtc = null;
        await context.SaveChangesAsync();

        var manualKey = TotpSecurity.ToBase32(secret);
        var issuer = Uri.EscapeDataString("CinemaFlow");
        var label = Uri.EscapeDataString($"CinemaFlow:{user.Email}");
        return new MfaSetupDto
        {
            ManualKey = manualKey,
            AuthenticatorUri = $"otpauth://totp/{label}?secret={manualKey}&issuer={issuer}&algorithm=SHA1&digits=6&period=30"
        };
    }

    public async Task<MfaRecoveryCodesDto?> EnableMfaAsync(Guid userId, string code, string currentAccessToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var credential = await context.MfaCredentials.SingleOrDefaultAsync(item => item.UserId == userId && !item.Enabled);
        if (credential is null || !VerifyMfaCode(credential, code, consumeRecoveryCode: false)) return null;
        var codes = Enumerable.Range(0, 8).Select(_ => Convert.ToHexString(RandomNumberGenerator.GetBytes(6))).ToList();
        credential.RecoveryCodeHashes = string.Join('|', codes.Select(CredentialSecurity.TokenHash));
        credential.Enabled = true;
        credential.EnabledAtUtc = UtcNow;
        var currentTokenHash = CredentialSecurity.IsToken(currentAccessToken) ? CredentialSecurity.TokenHash(currentAccessToken) : string.Empty;
        if (!string.IsNullOrEmpty(currentTokenHash) && await context.AccessTokens.AnyAsync(item => item.Token == currentTokenHash && item.UserId == userId))
            context.MfaVerifiedSessions.Add(new MfaVerifiedSessionEntity { TokenHash = currentTokenHash, UserId = userId, VerifiedAtUtc = UtcNow });
        await context.SaveChangesAsync();
        return new MfaRecoveryCodesDto { RecoveryCodes = codes };
    }

    public async Task<bool> DisableMfaAsync(Guid userId, string currentPassword, string code)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var user = await context.Users.SingleOrDefaultAsync(item => item.Id == userId);
        var security = await context.UserSecurity.SingleOrDefaultAsync(item => item.UserId == userId);
        var credential = await context.MfaCredentials.SingleOrDefaultAsync(item => item.UserId == userId && item.Enabled);
        if (user is null || security is null || credential is null ||
            !CredentialSecurity.Verify(currentPassword, user.PasswordSalt, user.PasswordHash, security.PasswordIterations) ||
            !VerifyMfaCode(credential, code, consumeRecoveryCode: true)) return false;
        context.MfaCredentials.Remove(credential);
        context.MfaChallenges.RemoveRange(await context.MfaChallenges.Where(item => item.UserId == userId).ToListAsync());
        context.MfaVerifiedSessions.RemoveRange(await context.MfaVerifiedSessions.Where(item => item.UserId == userId).ToListAsync());
        security.Version++;
        await context.SaveChangesAsync();
        return true;
    }

    private bool VerifyMfaCode(MfaCredentialEntity credential, string code, bool consumeRecoveryCode)
    {
        try
        {
            var secret = Convert.FromBase64String(mfaProtector.Unprotect(credential.ProtectedSecret));
            if (TotpSecurity.Verify(secret, code, UtcNow)) return true;
        }
        catch (CryptographicException) { return false; }
        catch (FormatException) { return false; }

        var normalized = TotpSecurity.NormalizeCode(code);
        var hash = CredentialSecurity.TokenHash(normalized);
        var hashes = credential.RecoveryCodeHashes.Split('|', StringSplitOptions.RemoveEmptyEntries).ToList();
        var match = hashes.FirstOrDefault(item => CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(item), System.Text.Encoding.UTF8.GetBytes(hash)));
        if (match is null) return false;
        if (consumeRecoveryCode) credential.RecoveryCodeHashes = string.Join('|', hashes.Where(item => item != match));
        return true;
    }
}
