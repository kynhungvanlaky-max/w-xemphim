using Moq;
using WebXemPhim.Client.Services;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Tests.ClientTests;

public sealed class AuthStateTests
{
    [Fact]
    public async Task EmptySessionIsGuestAndInitialized()
    {
        var api = new Mock<IAccountApiClient>(); var store = new Mock<IAuthSessionStore>();
        var auth = new AuthState(api.Object, store.Object);
        await auth.InitializeAsync();
        Assert.True(auth.IsInitialized); Assert.False(auth.IsAuthenticated);
        api.Verify(x=>x.GetMeResultAsync(), Times.Never);
    }
    [Fact]
    public async Task RestoreValidatesIdentityAndRunsOnce()
    {
        var api = new Mock<IAccountApiClient>(); var store = new Mock<IAuthSessionStore>();
        store.Setup(x=>x.ReadAsync()).ReturnsAsync("existing-token");
        api.Setup(x=>x.GetMeResultAsync()).ReturnsAsync(ApiResponse<UserDto>.Ok(new() { FullName="Nguyễn An" }));
        var auth = new AuthState(api.Object, store.Object);
        await Task.WhenAll(auth.InitializeAsync(), auth.InitializeAsync());
        Assert.True(auth.IsAuthenticated); Assert.Equal("Nguyễn An",auth.User!.FullName);
        Assert.False(auth.ApiUnavailable);
        api.Verify(x=>x.SetAccessToken("existing-token"),Times.Once);
        api.Verify(x=>x.GetMeResultAsync(),Times.Once);
    }
    [Fact]
    public async Task InvalidSessionIsCleared()
    {
        var api = new Mock<IAccountApiClient>(); var store = new Mock<IAuthSessionStore>();
        store.Setup(x=>x.ReadAsync()).ReturnsAsync("expired-token");
        api.Setup(x=>x.GetMeResultAsync()).ReturnsAsync(ApiResponse<UserDto>.Fail("Token hết hạn"));
        var auth = new AuthState(api.Object, store.Object); await auth.InitializeAsync();
        Assert.False(auth.IsAuthenticated); Assert.Null(auth.AccessToken);
        Assert.False(auth.ApiUnavailable);
        store.Verify(x=>x.ClearAsync(),Times.Once);
    }

    /// <summary>
    /// Khi API chưa bật (lần đầu mở trang lúc stack còn đang khởi động) thì phiên đăng nhập
    /// phải được giữ nguyên và giao diện tự thử lại — không được xoá phiên rồi đá người
    /// dùng ra trang đăng nhập.
    /// </summary>
    [Fact]
    public async Task UnreachableApiKeepsSessionAndFlagsUnavailable()
    {
        var api = new Mock<IAccountApiClient>(); var store = new Mock<IAuthSessionStore>();
        store.Setup(x=>x.ReadAsync()).ReturnsAsync("still-valid-token");
        api.Setup(x=>x.GetMeResultAsync()).ReturnsAsync((ApiResponse<UserDto>?)null);
        var auth = new AuthState(api.Object, store.Object);
        await auth.InitializeAsync();

        Assert.True(auth.IsInitialized);
        Assert.True(auth.ApiUnavailable);
        Assert.Equal("still-valid-token", auth.AccessToken);
        Assert.True(auth.HasSession);
        store.Verify(x=>x.ClearAsync(), Times.Never);
    }

    /// <summary>
    /// Khi API trả lời được thì trạng thái "chưa sẵn sàng" phải được gỡ bỏ và hồ sơ người
    /// dùng phải được nạp lại.
    /// </summary>
    [Fact]
    public async Task RefreshClearsUnavailableFlagWhenApiAnswers()
    {
        var api = new Mock<IAccountApiClient>(); var store = new Mock<IAuthSessionStore>();
        store.Setup(x=>x.ReadAsync()).ReturnsAsync("still-valid-token");
        api.SetupSequence(x=>x.GetMeResultAsync())
            .ReturnsAsync((ApiResponse<UserDto>?)null)
            .ReturnsAsync(ApiResponse<UserDto>.Ok(new() { FullName="Nguyễn An" }));
        var auth = new AuthState(api.Object, store.Object);
        await auth.InitializeAsync();
        Assert.True(auth.ApiUnavailable);

        await auth.RefreshAsync();
        Assert.False(auth.ApiUnavailable);
        Assert.True(auth.IsAuthenticated);
        Assert.Equal("Nguyễn An", auth.User!.FullName);
    }
    [Fact]
    public async Task LoginPersistsSessionAndLogoutClearsIt()
    {
        var api = new Mock<IAccountApiClient>(); var store = new Mock<IAuthSessionStore>();
        api.Setup(x=>x.LoginAsync(It.IsAny<LoginRequest>())).ReturnsAsync(ApiResponse<AuthDto>.Ok(new() { AccessToken="new-token", User=new() { FullName="An" } }));
        var auth = new AuthState(api.Object,store.Object);
        await auth.LoginAsync(new()); Assert.True(auth.IsAuthenticated);
        store.Verify(x=>x.WriteAsync("new-token"),Times.Once);
        await auth.LogoutAsync(); Assert.False(auth.IsAuthenticated);
        api.Verify(x=>x.LogoutAsync(),Times.Once);
        store.Verify(x=>x.ClearAsync(),Times.Once);
    }
    [Fact]
    public async Task FailedLoginNeverCreatesSession()
    {
        var api = new Mock<IAccountApiClient>(); var store = new Mock<IAuthSessionStore>();
        api.Setup(x=>x.LoginAsync(It.IsAny<LoginRequest>())).ReturnsAsync(ApiResponse<AuthDto>.Fail("Sai mật khẩu"));
        var auth = new AuthState(api.Object,store.Object); await auth.LoginAsync(new());
        Assert.False(auth.IsAuthenticated); store.Verify(x=>x.WriteAsync(It.IsAny<string>()),Times.Never);
    }
    [Theory]
    [InlineData("/vip", "/vip")]
    [InlineData("/watchlist", "/watchlist")]
    [InlineData("https://evil.example", "/watchlist")]
    [InlineData("//evil.example", "/watchlist")]
    [InlineData("/\\evil.example", "/watchlist")]
    [InlineData(null, "/watchlist")]
    public void ReturnUrlOnlyAllowsLocalPaths(string? input, string expected) => Assert.Equal(expected,AuthNavigation.ReturnPath(input));
}
