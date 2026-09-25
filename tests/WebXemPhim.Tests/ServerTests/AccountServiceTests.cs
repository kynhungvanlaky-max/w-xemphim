using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Requests;

namespace WebXemPhim.Tests.ServerTests;

public class AccountServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 2, 0, 0, TimeSpan.Zero);
    private readonly ManualTimeProvider _clock = new(Now);
    private readonly AccountService _service;

    public AccountServiceTests() => _service = new AccountService(_clock);

    [Fact]
    public async Task RegisterAsync_ValidRequestCreatesFreeAccountAndSession()
    {
        var result = await _service.RegisterAsync(ValidRegister());

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.NotEqual(Guid.Empty, result.Data.User.Id);
        Assert.Equal("Nguyễn Minh An", result.Data.User.FullName);
        Assert.Equal("an@example.com", result.Data.User.Email);
        Assert.Equal(MembershipTier.Free, result.Data.User.MembershipTier);
        Assert.Null(result.Data.User.SubscriptionExpiresAt);
        Assert.Equal(Now, result.Data.User.CreatedAt);
        Assert.False(string.IsNullOrWhiteSpace(result.Data.Token));
        Assert.Equal(Now.AddDays(7), result.Data.ExpiresAt);
    }

    [Fact]
    public async Task RegisterAsync_TrimsNameAndNormalizesEmail()
    {
        var request = ValidRegister();
        request.FullName = "  Nguyễn Minh An  ";
        request.Email = "  AN@Example.COM ";

        var result = await _service.RegisterAsync(request);

        Assert.Equal("Nguyễn Minh An", result.Data!.User.FullName);
        Assert.Equal("an@example.com", result.Data.User.Email);
    }

    [Fact]
    public async Task RegisterAsync_DuplicateEmailIsRejectedCaseInsensitively()
    {
        Assert.True((await _service.RegisterAsync(ValidRegister())).Success);
        var duplicate = ValidRegister();
        duplicate.Email = "AN@EXAMPLE.COM";

        var result = await _service.RegisterAsync(duplicate);

        Assert.False(result.Success);
        Assert.Equal("email_exists", result.ErrorCode);
    }

    [Theory]
    [InlineData("", "invalid_name")]
    [InlineData("A", "invalid_name")]
    [InlineData("not-an-email", "invalid_email")]
    public async Task RegisterAsync_InvalidIdentityFieldsAreRejected(string value, string errorCode)
    {
        var request = ValidRegister();
        if (errorCode == "invalid_name") request.FullName = value;
        else request.Email = value;

        var result = await _service.RegisterAsync(request);

        Assert.False(result.Success);
        Assert.Equal(errorCode, result.ErrorCode);
    }

    [Theory]
    [InlineData("Short1", "password_too_short")]
    [InlineData("lowercase1", "password_requires_uppercase")]
    [InlineData("UPPERCASE1", "password_requires_lowercase")]
    [InlineData("NoNumberHere", "password_requires_number")]
    public async Task RegisterAsync_WeakPasswordIsRejected(string password, string errorCode)
    {
        var request = ValidRegister();
        request.Password = password;
        request.ConfirmPassword = password;

        var result = await _service.RegisterAsync(request);

        Assert.False(result.Success);
        Assert.Equal(errorCode, result.ErrorCode);
    }

    [Fact]
    public async Task RegisterAsync_MismatchedConfirmationIsRejected()
    {
        var request = ValidRegister();
        request.ConfirmPassword = "Different123";

        var result = await _service.RegisterAsync(request);

        Assert.False(result.Success);
        Assert.Equal("password_mismatch", result.ErrorCode);
    }

    [Fact]
    public async Task RegisterAsync_TermsMustBeAccepted()
    {
        var request = ValidRegister();
        request.AcceptTerms = false;

        var result = await _service.RegisterAsync(request);

        Assert.False(result.Success);
        Assert.Equal("terms_required", result.ErrorCode);
    }

    [Fact]
    public async Task LoginAsync_ValidCredentialsReturnNewSession()
    {
        var registered = await _service.RegisterAsync(ValidRegister());

        var result = await _service.LoginAsync(new LoginRequest
        {
            Email = " AN@EXAMPLE.COM ",
            Password = "Cinema123"
        });

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.NotEqual(registered.Data!.Token, result.Data.Token);
        Assert.Equal(registered.Data.User.Id, result.Data.User.Id);
    }

    [Theory]
    [InlineData("missing@example.com", "Cinema123")]
    [InlineData("an@example.com", "WrongPassword1")]
    [InlineData("", "Cinema123")]
    public async Task LoginAsync_InvalidCredentialsUseSameGenericError(string email, string password)
    {
        await _service.RegisterAsync(ValidRegister());

        var result = await _service.LoginAsync(new LoginRequest { Email = email, Password = password });

        Assert.False(result.Success);
        Assert.Equal("invalid_credentials", result.ErrorCode);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task GetCurrentUserAsync_ValidTokenReturnsProfile()
    {
        var session = (await _service.RegisterAsync(ValidRegister())).Data!;

        var result = await _service.GetCurrentUserAsync(session.Token);

        Assert.True(result.Success);
        Assert.Equal(session.User.Id, result.Data!.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown-token")]
    public async Task GetCurrentUserAsync_MissingOrUnknownTokenIsUnauthorized(string? token)
    {
        var result = await _service.GetCurrentUserAsync(token);

        Assert.False(result.Success);
        Assert.Equal("unauthorized", result.ErrorCode);
    }

    [Fact]
    public async Task GetCurrentUserAsync_ExpiredSessionIsUnauthorizedAndRemoved()
    {
        var session = (await _service.RegisterAsync(ValidRegister())).Data!;
        _clock.Advance(TimeSpan.FromDays(8));

        var first = await _service.GetCurrentUserAsync(session.Token);
        var second = await _service.GetCurrentUserAsync(session.Token);

        Assert.False(first.Success);
        Assert.False(second.Success);
        Assert.Equal("session_expired", first.ErrorCode);
        Assert.Equal("unauthorized", second.ErrorCode);
    }

    [Fact]
    public async Task LogoutAsync_ValidTokenInvalidatesSession()
    {
        var session = (await _service.RegisterAsync(ValidRegister())).Data!;

        var logout = await _service.LogoutAsync(session.Token);
        var profile = await _service.GetCurrentUserAsync(session.Token);

        Assert.True(logout.Success);
        Assert.False(profile.Success);
    }

    [Fact]
    public async Task LogoutAsync_UnknownTokenIsIdempotent()
    {
        var result = await _service.LogoutAsync("unknown-token");

        Assert.True(result.Success);
        Assert.True(result.Data);
    }

    [Fact]
    public async Task RequestPasswordResetAsync_ExistingEmailReturnsExpiringOneTimeToken()
    {
        await _service.RegisterAsync(ValidRegister());

        var result = await _service.RequestPasswordResetAsync(new ForgotPasswordRequest
        {
            Email = "an@example.com"
        });

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.EmailAccepted);
        Assert.False(string.IsNullOrWhiteSpace(result.Data.DevelopmentResetToken));
        Assert.Equal(Now.AddMinutes(15), result.Data.ExpiresAt);
    }

    [Fact]
    public async Task RequestPasswordResetAsync_UnknownEmailDoesNotLeakAccountExistence()
    {
        var result = await _service.RequestPasswordResetAsync(new ForgotPasswordRequest
        {
            Email = "missing@example.com"
        });

        Assert.True(result.Success);
        Assert.True(result.Data!.EmailAccepted);
        Assert.Null(result.Data.DevelopmentResetToken);
        Assert.Null(result.Data.ExpiresAt);
    }

    [Fact]
    public async Task ResetPasswordAsync_ValidTokenChangesPasswordAndConsumesToken()
    {
        await _service.RegisterAsync(ValidRegister());
        var resetToken = (await _service.RequestPasswordResetAsync(new ForgotPasswordRequest
        {
            Email = "an@example.com"
        })).Data!.DevelopmentResetToken!;
        var request = new ResetPasswordRequest
        {
            Email = "an@example.com",
            Token = resetToken,
            NewPassword = "NewCinema456",
            ConfirmPassword = "NewCinema456"
        };

        var reset = await _service.ResetPasswordAsync(request);
        var oldLogin = await _service.LoginAsync(new LoginRequest { Email = request.Email, Password = "Cinema123" });
        var newLogin = await _service.LoginAsync(new LoginRequest { Email = request.Email, Password = request.NewPassword });
        var reuse = await _service.ResetPasswordAsync(request);

        Assert.True(reset.Success);
        Assert.False(oldLogin.Success);
        Assert.True(newLogin.Success);
        Assert.False(reuse.Success);
        Assert.Equal("invalid_reset_token", reuse.ErrorCode);
    }

    [Theory]
    [InlineData("wrong-token", "NewCinema456", "NewCinema456", "invalid_reset_token")]
    [InlineData("TOKEN", "NewCinema456", "Mismatch456", "password_mismatch")]
    [InlineData("TOKEN", "weak", "weak", "password_too_short")]
    public async Task ResetPasswordAsync_InvalidRequestIsRejected(
        string token,
        string password,
        string confirmation,
        string expectedError)
    {
        await _service.RegisterAsync(ValidRegister());
        var realToken = (await _service.RequestPasswordResetAsync(new ForgotPasswordRequest
        {
            Email = "an@example.com"
        })).Data!.DevelopmentResetToken!;

        var result = await _service.ResetPasswordAsync(new ResetPasswordRequest
        {
            Email = "an@example.com",
            Token = token == "TOKEN" ? realToken : token,
            NewPassword = password,
            ConfirmPassword = confirmation
        });

        Assert.False(result.Success);
        Assert.Equal(expectedError, result.ErrorCode);
    }

    [Fact]
    public async Task ResetPasswordAsync_ExpiredTokenIsRejected()
    {
        await _service.RegisterAsync(ValidRegister());
        var token = (await _service.RequestPasswordResetAsync(new ForgotPasswordRequest
        {
            Email = "an@example.com"
        })).Data!.DevelopmentResetToken!;
        _clock.Advance(TimeSpan.FromMinutes(16));

        var result = await _service.ResetPasswordAsync(new ResetPasswordRequest
        {
            Email = "an@example.com",
            Token = token,
            NewPassword = "NewCinema456",
            ConfirmPassword = "NewCinema456"
        });

        Assert.False(result.Success);
        Assert.Equal("reset_token_expired", result.ErrorCode);
    }

    [Fact]
    public void GetVipPlans_ReturnsStablePlansWithMonthlyAndAnnualPricing()
    {
        var plans = _service.GetVipPlans();

        Assert.Equal(2, plans.Count);
        Assert.Equal(plans.Count, plans.Select(plan => plan.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(plans, plan =>
        {
            Assert.True(plan.MonthlyPrice > 0);
            Assert.True(plan.YearlyPrice > plan.MonthlyPrice);
            Assert.NotEmpty(plan.Features);
        });
        Assert.Contains(plans, plan => plan.Tier == MembershipTier.Vip);
        Assert.Contains(plans, plan => plan.Tier == MembershipTier.Premium);
    }

    [Theory]
    [InlineData("vip", BillingCycle.Monthly, MembershipTier.Vip, 1)]
    [InlineData("VIP", BillingCycle.Yearly, MembershipTier.Vip, 12)]
    [InlineData("premium", BillingCycle.Monthly, MembershipTier.Premium, 1)]
    [InlineData("PREMIUM", BillingCycle.Yearly, MembershipTier.Premium, 12)]
    public async Task SubscribeVipAsync_ValidPlanUpgradesProfile(
        string planCode,
        BillingCycle cycle,
        MembershipTier tier,
        int months)
    {
        var session = (await _service.RegisterAsync(ValidRegister())).Data!;

        var result = await _service.SubscribeVipAsync(session.Token, new SubscribeVipRequest
        {
            PlanCode = planCode,
            BillingCycle = cycle
        });

        Assert.True(result.Success);
        Assert.Equal(tier, result.Data!.User.MembershipTier);
        Assert.Equal(Now.AddMonths(months), result.Data.User.SubscriptionExpiresAt);
        Assert.Equal(cycle, result.Data.BillingCycle);
        Assert.True(result.Data.AmountPaid > 0);
    }

    [Fact]
    public async Task SubscribeVipAsync_RenewalExtendsFromCurrentExpiry()
    {
        var session = (await _service.RegisterAsync(ValidRegister())).Data!;
        var request = new SubscribeVipRequest { PlanCode = "vip", BillingCycle = BillingCycle.Monthly };
        await _service.SubscribeVipAsync(session.Token, request);

        var renewed = await _service.SubscribeVipAsync(session.Token, request);

        Assert.Equal(Now.AddMonths(2), renewed.Data!.User.SubscriptionExpiresAt);
    }

    [Theory]
    [InlineData("unknown", BillingCycle.Monthly, "invalid_plan")]
    [InlineData("vip", (BillingCycle)99, "invalid_billing_cycle")]
    public async Task SubscribeVipAsync_InvalidPlanOrCycleIsRejected(
        string plan,
        BillingCycle cycle,
        string expectedError)
    {
        var session = (await _service.RegisterAsync(ValidRegister())).Data!;

        var result = await _service.SubscribeVipAsync(session.Token, new SubscribeVipRequest
        {
            PlanCode = plan,
            BillingCycle = cycle
        });

        Assert.False(result.Success);
        Assert.Equal(expectedError, result.ErrorCode);
    }

    [Fact]
    public async Task SubscribeVipAsync_RequiresValidSession()
    {
        var result = await _service.SubscribeVipAsync("invalid", new SubscribeVipRequest
        {
            PlanCode = "vip",
            BillingCycle = BillingCycle.Monthly
        });

        Assert.False(result.Success);
        Assert.Equal("unauthorized", result.ErrorCode);
    }

    private static RegisterRequest ValidRegister() => new()
    {
        FullName = "Nguyễn Minh An",
        Email = "an@example.com",
        Password = "Cinema123",
        ConfirmPassword = "Cinema123",
        AcceptTerms = true
    };

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _utcNow = now;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan value) => _utcNow += value;
    }
}
