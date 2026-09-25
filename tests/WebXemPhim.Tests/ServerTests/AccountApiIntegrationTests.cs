using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using WebXemPhim.Server.Security;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;
namespace WebXemPhim.Tests.ServerTests;

public sealed class AccountApiIntegrationTests : IClassFixture<TestApiFactory>
{
    private readonly HttpClient _client;
    private readonly TestResetEmailSender _emailSender;

    public AccountApiIntegrationTests(TestApiFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        _emailSender = (TestResetEmailSender)factory.Services.GetRequiredService<IPasswordResetEmailSender>();
    }

    [Fact]
    public async Task RegisterLoginAndProfileLifecycleWorksOverHttp()
    {
        var email = UniqueEmail();
        var registration = await Register(email);

        Assert.Equal(email, registration.User.Email);
        Assert.Equal(MembershipTier.Free, registration.User.Membership);
        Assert.False(string.IsNullOrWhiteSpace(registration.AccessToken));

        var loginHttp = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email.ToUpperInvariant(), Password = "Cinema123" });
        var login = await Read<ApiResponse<AuthDto>>(loginHttp);
        Assert.Equal(HttpStatusCode.OK, loginHttp.StatusCode);
        Assert.NotEqual(registration.AccessToken, login.Data!.AccessToken);

        Authorize(login.Data.AccessToken);
        var profile = await GetSuccess<UserDto>("/api/auth/me");
        Assert.Equal(registration.User.Id, profile.Id);
    }

    [Fact]
    public async Task RegisterDuplicateEmailReturnsConflict()
    {
        var email = UniqueEmail();
        await Register(email);

        var response = await _client.PostAsJsonAsync("/api/auth/register", ValidRegistration(email.ToUpperInvariant()));
        var payload = await Read<ApiResponse<AuthDto>>(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.False(payload.Success);
        Assert.Null(payload.Data);
    }

    [Theory]
    [InlineData("bad-email", "Cinema123", "Cinema123")]
    [InlineData("valid@example.com", "short", "short")]
    [InlineData("valid@example.com", "Cinema123", "Different123")]
    public async Task RegisterInvalidPayloadReturnsValidationProblem(string email, string password, string confirmation)
    {
        var request = ValidRegistration(email);
        request.Password = password;
        request.ConfirmPassword = confirmation;

        var response = await _client.PostAsJsonAsync("/api/auth/register", request);
        var problem = await Read<ValidationProblemDetails>(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEmpty(problem.Errors);
    }

    [Fact]
    public async Task LoginWrongPasswordReturnsUnauthorizedEnvelope()
    {
        var email = UniqueEmail();
        await Register(email);

        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = "WrongPassword1" });
        var payload = await Read<ApiResponse<AuthDto>>(response);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(payload.Success);
    }

    [Fact]
    public async Task ProtectedEndpointsRejectMissingAndInvalidBearerTokens()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/payments/history")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PutAsJsonAsync("/api/reviews/movie/1", new UpsertReviewRequest { Rating = 8 })).StatusCode);

        Authorize("not-a-valid-token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task ForgotAndResetPasswordAllowsNewPasswordOnlyOnce()
    {
        var email = UniqueEmail();
        await Register(email);
        var forgotHttp = await _client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest { Email = email });
        var forgot = await Read<ApiResponse<PasswordResetDto?>>(forgotHttp);
        Assert.Equal(HttpStatusCode.OK, forgotHttp.StatusCode);
        Assert.Null(forgot.Data);

        var reset = new ResetPasswordRequest { Email = email, Token = _emailSender.Tokens[email], NewPassword = "NewCinema456", ConfirmPassword = "NewCinema456" };
        var firstReset = await _client.PostAsJsonAsync("/api/auth/reset-password", reset);
        var reusedReset = await _client.PostAsJsonAsync("/api/auth/reset-password", reset);
        var oldLogin = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = "Cinema123" });
        var newLogin = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = "NewCinema456" });

        Assert.Equal(HttpStatusCode.OK, firstReset.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, reusedReset.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
    }

    [Fact]
    public async Task ForgotPasswordUnknownEmailDoesNotRevealAccountExistence()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest { Email = UniqueEmail() });
        var payload = await Read<ApiResponse<PasswordResetDto?>>(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(payload.Success);
        Assert.Null(payload.Data);
    }

    [Fact]
    public async Task VipPlansArePublicAndHaveOneRecommendedPlan()
    {
        var plans = await GetSuccess<List<VipPlanDto>>("/api/payments/plans");

        Assert.Equal(4, plans.Count);
        Assert.Single(plans, plan => plan.IsRecommended);
        Assert.All(plans, plan =>
        {
            Assert.True(plan.Price > 0);
            Assert.True(plan.DurationDays > 0);
            Assert.NotEmpty(plan.Benefits);
        });
    }

    [Fact]
    public async Task CheckoutRequiresAuthenticationAndValidPlan()
    {
        var request = new CreatePaymentRequest { PlanId = "vip-30", Method = PaymentMethod.Momo };
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsJsonAsync("/api/payments/checkout", request)).StatusCode);

        Authorize((await Register(UniqueEmail())).AccessToken);
        request.PlanId = "not-a-plan";
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/payments/checkout", request)).StatusCode);
    }

    [Fact]
    public async Task CheckoutRemainsPendingWithoutVerifiedPaymentAndAddsHistory()
    {
        var auth = await Register(UniqueEmail());
        Authorize(auth.AccessToken);

        var checkoutHttp = await _client.PostAsJsonAsync("/api/payments/checkout", new CreatePaymentRequest { PlanId = "vip-90", Method = PaymentMethod.Visa });
        var checkout = await Read<ApiResponse<PaymentDto>>(checkoutHttp);
        var profile = await GetSuccess<UserDto>("/api/auth/me");
        var history = await GetSuccess<List<PaymentDto>>("/api/payments/history");

        Assert.Equal(HttpStatusCode.OK, checkoutHttp.StatusCode);
        Assert.Equal(PaymentStatus.Pending, checkout.Data!.Status);
        Assert.Equal(199000, checkout.Data.Amount);
        Assert.False(profile.IsVip);
        Assert.Null(checkout.Data.PaidAt);
        Assert.Contains(history, payment => payment.Id == checkout.Data.Id);
    }

    [Fact]
    public async Task ReviewsUnknownMovieReturnsNotFound()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/reviews/movie/9999")).StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public async Task ReviewRatingOutsideRangeReturnsValidationProblem(int rating)
    {
        Authorize((await Register(UniqueEmail())).AccessToken);
        var response = await _client.PutAsJsonAsync("/api/reviews/movie/1", new UpsertReviewRequest { Rating = rating });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReviewCreateUpdateDeleteLifecycleKeepsOneReviewPerUser()
    {
        const int movieId = 12;
        var auth = await Register(UniqueEmail());
        Authorize(auth.AccessToken);
        var before = await GetSuccess<MovieReviewsDto>($"/api/reviews/movie/{movieId}");

        var created = await PutReview(movieId, 8, "Hình ảnh tốt, nhịp phim chắc.");
        var updated = await PutReview(movieId, 10, "Một tập phim rất đáng nhớ.");
        var afterUpdate = await GetSuccess<MovieReviewsDto>($"/api/reviews/movie/{movieId}");

        Assert.Equal(created.Id, updated.Id);
        Assert.Equal(10, updated.Rating);
        Assert.Equal(before.TotalReviews + 1, afterUpdate.TotalReviews);
        Assert.Single(afterUpdate.Reviews, review => review.UserId == auth.User.Id);

        var deleted = await _client.DeleteAsync($"/api/reviews/movie/{movieId}");
        var afterDelete = await GetSuccess<MovieReviewsDto>($"/api/reviews/movie/{movieId}");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Equal(before.TotalReviews, afterDelete.TotalReviews);
    }

    private async Task<ReviewDto> PutReview(int movieId, int rating, string comment)
    {
        var response = await _client.PutAsJsonAsync($"/api/reviews/movie/{movieId}", new UpsertReviewRequest { Rating = rating, Comment = comment });
        var payload = await Read<ApiResponse<ReviewDto>>(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return payload.Data!;
    }

    private async Task<AuthDto> Register(string email)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", ValidRegistration(email));
        var payload = await Read<ApiResponse<AuthDto>>(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return payload.Data!;
    }

    private static RegisterRequest ValidRegistration(string email) => new()
    {
        FullName = "Nguyễn Minh An",
        Email = email,
        Password = "Cinema123",
        ConfirmPassword = "Cinema123",
        AcceptTerms = true
    };

    private void Authorize(string token) => _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@example.com";

    private async Task<T> GetSuccess<T>(string uri)
    {
        var response = await _client.GetAsync(uri);
        var payload = await Read<ApiResponse<T>>(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(payload.Success);
        return payload.Data!;
    }

    private static async Task<T> Read<T>(HttpResponseMessage response) =>
        Assert.IsType<T>(await response.Content.ReadFromJsonAsync<T>());
}
