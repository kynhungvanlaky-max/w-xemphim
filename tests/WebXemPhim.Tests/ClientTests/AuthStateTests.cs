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
        api.Verify(x=>x.GetMeAsync(), Times.Never);
    }
    [Fact]
    public async Task RestoreValidatesIdentityAndRunsOnce()
    {
        var api = new Mock<IAccountApiClient>(); var store = new Mock<IAuthSessionStore>();
        store.Setup(x=>x.ReadAsync()).ReturnsAsync("existing-token");
        api.Setup(x=>x.GetMeAsync()).ReturnsAsync(new UserDto { FullName="Nguyễn An" });
        var auth = new AuthState(api.Object, store.Object);
        await Task.WhenAll(auth.InitializeAsync(), auth.InitializeAsync());
        Assert.True(auth.IsAuthenticated); Assert.Equal("Nguyễn An",auth.User!.FullName);
        api.Verify(x=>x.SetAccessToken("existing-token"),Times.Once);
        api.Verify(x=>x.GetMeAsync(),Times.Once);
    }
    [Fact]
    public async Task InvalidSessionIsCleared()
    {
        var api = new Mock<IAccountApiClient>(); var store = new Mock<IAuthSessionStore>();
        store.Setup(x=>x.ReadAsync()).ReturnsAsync("expired-token");
        var auth = new AuthState(api.Object, store.Object); await auth.InitializeAsync();
        Assert.False(auth.IsAuthenticated); Assert.Null(auth.AccessToken);
        store.Verify(x=>x.ClearAsync(),Times.Once);
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
