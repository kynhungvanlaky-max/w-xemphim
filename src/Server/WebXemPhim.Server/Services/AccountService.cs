using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;

namespace WebXemPhim.Server.Services;

public record ServiceResult<T>(bool Success, T? Data, string? ErrorCode = null)
{
    public static ServiceResult<T> Ok(T data) => new(true, data);
    public static ServiceResult<T> Fail(string code) => new(false, default, code);
}

public sealed class AccountService(TimeProvider timeProvider)
{
    private sealed class Account
    {
        public UserDto Profile { get; init; } = new();
        public byte[] Salt { get; set; } = [];
        public byte[] Hash { get; set; } = [];
    }

    private sealed record TokenRecord(Guid UserId, DateTimeOffset ExpiresAt);

    private readonly Dictionary<string, Account> _accounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TokenRecord> _sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TokenRecord> _resetTokens = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    private static readonly List<AccountVipPlanDto> VipPlans =
    [
        new() { Code = "vip", Name = "VIP", Tier = MembershipTier.Vip, MonthlyPrice = 59000, YearlyPrice = 590000, Features = ["Full HD", "Không quảng cáo", "Ba thiết bị"] },
        new() { Code = "premium", Name = "Premium 4K", Tier = MembershipTier.Premium, MonthlyPrice = 99000, YearlyPrice = 990000, Features = ["4K HDR", "Âm thanh cao cấp", "Năm thiết bị", "Suất chiếu sớm"] }
    ];

    public Task<ServiceResult<AuthDto>> RegisterAsync(RegisterRequest request)
    {
        var validation = ValidateRegistration(request);
        if (validation is not null) return Task.FromResult(ServiceResult<AuthDto>.Fail(validation));

        var email = NormalizeEmail(request.Email);
        lock (_lock)
        {
            if (_accounts.ContainsKey(email)) return Task.FromResult(ServiceResult<AuthDto>.Fail("email_exists"));

            var salt = RandomNumberGenerator.GetBytes(16);
            var profile = new UserDto
            {
                Id = Guid.NewGuid(),
                FullName = request.FullName.Trim(),
                Email = email,
                MembershipTier = MembershipTier.Free,
                CreatedAt = timeProvider.GetUtcNow(),
                Initials = Initials(request.FullName)
            };
            _accounts[email] = new Account { Profile = profile, Salt = salt, Hash = Hash(request.Password, salt) };
            return Task.FromResult(ServiceResult<AuthDto>.Ok(CreateSession(profile)));
        }
    }

    public Task<ServiceResult<AuthDto>> LoginAsync(LoginRequest request)
    {
        lock (_lock)
        {
            var email = NormalizeEmail(request.Email);
            if (!_accounts.TryGetValue(email, out var account) ||
                !CryptographicOperations.FixedTimeEquals(Hash(request.Password, account.Salt), account.Hash))
                return Task.FromResult(ServiceResult<AuthDto>.Fail("invalid_credentials"));

            return Task.FromResult(ServiceResult<AuthDto>.Ok(CreateSession(account.Profile)));
        }
    }

    public Task<ServiceResult<UserDto>> GetCurrentUserAsync(string? token)
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(token) || !_sessions.TryGetValue(token, out var session))
                return Task.FromResult(ServiceResult<UserDto>.Fail("unauthorized"));
            if (session.ExpiresAt <= timeProvider.GetUtcNow())
            {
                _sessions.Remove(token);
                return Task.FromResult(ServiceResult<UserDto>.Fail("session_expired"));
            }

            var profile = _accounts.Values.First(account => account.Profile.Id == session.UserId).Profile;
            return Task.FromResult(ServiceResult<UserDto>.Ok(profile));
        }
    }

    public Task<ServiceResult<bool>> LogoutAsync(string? token)
    {
        lock (_lock)
        {
            if (!string.IsNullOrWhiteSpace(token)) _sessions.Remove(token);
            return Task.FromResult(ServiceResult<bool>.Ok(true));
        }
    }

    public Task<ServiceResult<ForgotPasswordDto>> RequestPasswordResetAsync(ForgotPasswordRequest request)
    {
        lock (_lock)
        {
            var result = new ForgotPasswordDto { EmailAccepted = true };
            if (!_accounts.TryGetValue(NormalizeEmail(request.Email), out var account))
                return Task.FromResult(ServiceResult<ForgotPasswordDto>.Ok(result));

            var token = NewToken();
            var expiry = timeProvider.GetUtcNow().AddMinutes(15);
            _resetTokens[token] = new TokenRecord(account.Profile.Id, expiry);
            result.DevelopmentResetToken = token;
            result.ExpiresAt = expiry;
            return Task.FromResult(ServiceResult<ForgotPasswordDto>.Ok(result));
        }
    }

    public Task<ServiceResult<bool>> ResetPasswordAsync(ResetPasswordRequest request)
    {
        if (request.NewPassword != request.ConfirmPassword)
            return Task.FromResult(ServiceResult<bool>.Fail("password_mismatch"));
        var passwordError = ValidatePassword(request.NewPassword);
        if (passwordError is not null) return Task.FromResult(ServiceResult<bool>.Fail(passwordError));

        lock (_lock)
        {
            if (!_resetTokens.TryGetValue(request.Token, out var reset))
                return Task.FromResult(ServiceResult<bool>.Fail("invalid_reset_token"));
            _resetTokens.Remove(request.Token);
            if (reset.ExpiresAt <= timeProvider.GetUtcNow())
                return Task.FromResult(ServiceResult<bool>.Fail("reset_token_expired"));

            var account = _accounts.Values.FirstOrDefault(item => item.Profile.Id == reset.UserId);
            if (account is null || !account.Profile.Email.Equals(NormalizeEmail(request.Email), StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(ServiceResult<bool>.Fail("invalid_reset_token"));

            var salt = RandomNumberGenerator.GetBytes(16);
            account.Salt = salt;
            account.Hash = Hash(request.NewPassword, salt);
            return Task.FromResult(ServiceResult<bool>.Ok(true));
        }
    }

    public IReadOnlyList<AccountVipPlanDto> GetVipPlans() => VipPlans;

    public async Task<ServiceResult<SubscriptionDto>> SubscribeVipAsync(string? token, SubscribeVipRequest request)
    {
        var current = await GetCurrentUserAsync(token);
        if (!current.Success) return ServiceResult<SubscriptionDto>.Fail("unauthorized");
        if (!Enum.IsDefined(request.BillingCycle)) return ServiceResult<SubscriptionDto>.Fail("invalid_billing_cycle");
        var plan = VipPlans.FirstOrDefault(item => item.Code.Equals(request.PlanCode, StringComparison.OrdinalIgnoreCase));
        if (plan is null) return ServiceResult<SubscriptionDto>.Fail("invalid_plan");

        lock (_lock)
        {
            var profile = current.Data!;
            var start = profile.SubscriptionExpiresAt > timeProvider.GetUtcNow()
                ? profile.SubscriptionExpiresAt.Value
                : timeProvider.GetUtcNow();
            profile.MembershipTier = plan.Tier;
            profile.SubscriptionExpiresAt = start.AddMonths(request.BillingCycle == BillingCycle.Monthly ? 1 : 12);
            return ServiceResult<SubscriptionDto>.Ok(new SubscriptionDto
            {
                User = profile,
                BillingCycle = request.BillingCycle,
                AmountPaid = request.BillingCycle == BillingCycle.Monthly ? plan.MonthlyPrice : plan.YearlyPrice
            });
        }
    }

    private AuthDto CreateSession(UserDto user)
    {
        var token = NewToken();
        var expiry = timeProvider.GetUtcNow().AddDays(7);
        _sessions[token] = new TokenRecord(user.Id, expiry);
        return new AuthDto { Token = token, ExpiresAt = expiry, User = user };
    }

    private static string? ValidateRegistration(RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Trim().Length < 2) return "invalid_name";
        if (!new EmailAddressAttribute().IsValid(request.Email?.Trim())) return "invalid_email";
        var password = ValidatePassword(request.Password);
        if (password is not null) return password;
        if (request.Password != request.ConfirmPassword) return "password_mismatch";
        return request.AcceptTerms ? null : "terms_required";
    }

    private static string? ValidatePassword(string password)
    {
        if (password.Length < 8) return "password_too_short";
        if (!password.Any(char.IsUpper)) return "password_requires_uppercase";
        if (!password.Any(char.IsLower)) return "password_requires_lowercase";
        return password.Any(char.IsDigit) ? null : "password_requires_number";
    }

    private static string NormalizeEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();
    private static byte[] Hash(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, 100_000, HashAlgorithmName.SHA256, 32);
    private static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private static string Initials(string name) => string.Concat(name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).TakeLast(2).Select(word => char.ToUpperInvariant(word[0])));
}
