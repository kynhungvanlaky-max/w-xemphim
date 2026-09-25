using WebXemPhim.Shared.Enums;

namespace WebXemPhim.Shared.Models;

public class UserDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public MembershipTier MembershipTier { get; set; } = MembershipTier.Free;
    public MembershipTier Membership { get => MembershipTier; set => MembershipTier = value; }
    public DateTimeOffset? SubscriptionExpiresAt { get; set; }
    public DateTimeOffset? VipExpiresAt
    {
        get => SubscriptionExpiresAt;
        set => SubscriptionExpiresAt = value;
    }
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsVip => MembershipTier != MembershipTier.Free && SubscriptionExpiresAt > DateTimeOffset.UtcNow;
    public string Initials { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public bool IsLocked { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public bool MfaEnabled { get; set; }
    public bool MfaSessionVerified { get; set; }
}

public class AuthDto
{
    public string Token { get; set; } = string.Empty;
    public string AccessToken { get => Token; set => Token = value; }
    public DateTimeOffset ExpiresAt { get; set; }
    public UserDto User { get; set; } = new();
    public bool RequiresMfa { get; set; }
    public string MfaChallengeToken { get; set; } = string.Empty;
}

public sealed class MfaStatusDto
{
    public bool Enabled { get; set; }
    public bool SetupPending { get; set; }
}

public sealed class MfaSetupDto
{
    public string ManualKey { get; set; } = string.Empty;
    public string AuthenticatorUri { get; set; } = string.Empty;
}

public sealed class MfaRecoveryCodesDto
{
    public List<string> RecoveryCodes { get; set; } = [];
}

public class PasswordResetDto
{
    public string ResetToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}

public class ForgotPasswordDto
{
    public bool EmailAccepted { get; set; }
    public string? DevelopmentResetToken { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

public class AccountVipPlanDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public MembershipTier Tier { get; set; }
    public decimal MonthlyPrice { get; set; }
    public decimal YearlyPrice { get; set; }
    public List<string> Features { get; set; } = [];
}

public class SubscriptionDto
{
    public UserDto User { get; set; } = new();
    public BillingCycle BillingCycle { get; set; }
    public decimal AmountPaid { get; set; }
}

public class VipPlanDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int DurationDays { get; set; }
    public decimal Price { get; set; }
    public decimal? OriginalPrice { get; set; }
    public string? DiscountBadge { get; set; }
    public string Description { get; set; } = string.Empty;
    public List<string> Benefits { get; set; } = [];
    public bool IsRecommended { get; set; }
    public bool IsActive { get; set; } = true;
}

public class PaymentDto
{
    public Guid Id { get; set; }
    public string OrderCode { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string? UserEmail { get; set; }
    public string? UserFullName { get; set; }
    public string PlanId { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public PaymentStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? VipExpiresAt { get; set; }
    public string? Note { get; set; }
    public string? TransactionReference { get; set; }
}

public class ReviewDto
{
    public Guid Id { get; set; }
    public int MovieId { get; set; }
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserInitials { get; set; } = string.Empty;
    public int Rating { get; set; }
    public string Comment { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class MovieReviewsDto
{
    public int MovieId { get; set; }
    public double AverageRating { get; set; }
    public int TotalReviews { get; set; }
    public List<ReviewDto> Reviews { get; set; } = [];
}
