using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Tests.ServerTests;

public sealed class LibraryApiIntegrationTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    [Fact]
    public async Task GuestCannotReadOrModifyPersonalLibrary()
    {
        using var client=factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/library")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.PostAsync("/api/library/1",null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.DeleteAsync("/api/library/1")).StatusCode);
    }
    [Fact]
    public async Task SavedFilmsAreIsolatedAndPersistAcrossLogin()
    {
        using var first=factory.CreateClient(); using var second=factory.CreateClient();
        var email=$"library-{Guid.NewGuid():N}@example.com";
        await Register(first,email); await Register(second,$"other-{Guid.NewGuid():N}@example.com");
        Assert.Empty(await ReadList(first));
        (await first.PostAsync("/api/library/1",null)).EnsureSuccessStatusCode();
        (await first.PostAsync("/api/library/1",null)).EnsureSuccessStatusCode();
        Assert.Single(await ReadList(first)); Assert.Empty(await ReadList(second));
        await second.DeleteAsync("/api/library/1"); Assert.Single(await ReadList(first));
        using var reconnected=factory.CreateClient();
        var login=await (await reconnected.PostAsJsonAsync("/api/auth/login",new LoginRequest { Email=email,Password="Cinema123" })).Content.ReadFromJsonAsync<ApiResponse<AuthDto>>();
        reconnected.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",login!.Data!.AccessToken);
        Assert.Single(await ReadList(reconnected));
        await reconnected.DeleteAsync("/api/library/1"); Assert.Empty(await ReadList(first));
    }
    [Fact]
    public async Task MissingMovieCannotBeSaved()
    {
        using var client=factory.CreateClient(); await Register(client,$"missing-{Guid.NewGuid():N}@example.com");
        Assert.Equal(HttpStatusCode.NotFound,(await client.PostAsync("/api/library/999999",null)).StatusCode);
    }
    private static async Task Register(HttpClient client,string email)
    {
        var response=await client.PostAsJsonAsync("/api/auth/register",new RegisterRequest {FullName="Nguyễn An",Email=email,Password="Cinema123",ConfirmPassword="Cinema123",AcceptTerms=true});
        response.EnsureSuccessStatusCode();
        var payload=await response.Content.ReadFromJsonAsync<ApiResponse<AuthDto>>();
        client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",payload!.Data!.AccessToken);
    }
    private static async Task<List<WatchlistItemDto>> ReadList(HttpClient client) => (await client.GetFromJsonAsync<ApiResponse<List<WatchlistItemDto>>>("/api/library"))!.Data!;
}
