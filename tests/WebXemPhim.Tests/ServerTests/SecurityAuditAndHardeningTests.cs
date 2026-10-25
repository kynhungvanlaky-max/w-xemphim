using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using WebXemPhim.Server.Controllers;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Security;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;
using Xunit;

namespace WebXemPhim.Tests.ServerTests;

public sealed class SecurityAuditAndHardeningTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private readonly PostgresTestDatabaseFixture _fixture;

    public SecurityAuditAndHardeningTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private PostgresTestDatabase Database => _fixture.Database;

    private async Task<(AuthController controller, IAuthService authService, AppDbContext queryDb, NpgsqlConnection conn)> CreateTestEnvAsync()
    {
        var (connection, queryDb, factory) = await Database.CreateOpenContextAsync();
        var timeProvider = TimeProvider.System;
        var dataProtection = new EphemeralDataProtectionProvider();
        var authService = new AuthService(factory, timeProvider, dataProtection);
        var auditService = new SecurityAuditService(factory, timeProvider);
        var emailSenderMock = new Mock<IPasswordResetEmailSender>();
        var loggerMock = new Mock<ILogger<AuthController>>();

        var controller = new AuthController(
            authService,
            emailSenderMock.Object,
            auditService,
            loggerMock.Object);

        return (controller, authService, queryDb, connection);
    }

    [Fact]
    public async Task Register_WhenSuccessful_RecordsRegisterSuccessAuditEvent()
    {
        var (controller, _, queryDb, connection) = await CreateTestEnvAsync();
        await using (connection)
        await using (queryDb)
        {
            var req = new RegisterRequest
            {
                FullName = "Nguyễn Kiểm Toán",
                Email = "audit_register@cinemaflow.vn",
                Password = "P@sswordSecure2026!",
                ConfirmPassword = "P@sswordSecure2026!"
            };

            var actionResult = await controller.Register(req);
            var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
            var response = Assert.IsType<ApiResponse<AuthDto>>(okResult.Value);
            Assert.True(response.Success);
            Assert.NotNull(response.Data);

            var auditEvent = await queryDb.SecurityAuditEvents
                .FirstOrDefaultAsync(e => e.EventType == "register.success");

            Assert.NotNull(auditEvent);
            Assert.True(auditEvent.Success);
            Assert.Equal(response.Data.User.Id, auditEvent.UserId);
            Assert.Contains("audit_register@cinemaflow.vn", auditEvent.Detail);
        }
    }

    [Fact]
    public async Task Login_WhenSuccessful_RecordsLoginSuccessAuditEvent()
    {
        var (controller, _, queryDb, connection) = await CreateTestEnvAsync();
        await using (connection)
        await using (queryDb)
        {
            var registerReq = new RegisterRequest
            {
                FullName = "Đăng Nhập Thành Công",
                Email = "login_success@cinemaflow.vn",
                Password = "P@sswordSecure2026!",
                ConfirmPassword = "P@sswordSecure2026!"
            };
            var regResult = await controller.Register(registerReq);
            var regResponse = (ApiResponse<AuthDto>)((OkObjectResult)regResult.Result!).Value!;

            var loginReq = new LoginRequest
            {
                Email = "login_success@cinemaflow.vn",
                Password = "P@sswordSecure2026!"
            };

            var loginResult = await controller.Login(loginReq);
            var okLogin = Assert.IsType<OkObjectResult>(loginResult.Result);
            var loginResponse = Assert.IsType<ApiResponse<AuthDto>>(okLogin.Value);
            Assert.True(loginResponse.Success);

            var auditEvent = await queryDb.SecurityAuditEvents
                .FirstOrDefaultAsync(e => e.EventType == "login.success");

            Assert.NotNull(auditEvent);
            Assert.True(auditEvent.Success);
            Assert.Equal(regResponse.Data!.User.Id, auditEvent.UserId);
            Assert.Contains("login_success@cinemaflow.vn", auditEvent.Detail);
        }
    }

    [Fact]
    public async Task Login_WhenFailed_RecordsLoginFailedAuditEvent()
    {
        var (controller, _, queryDb, connection) = await CreateTestEnvAsync();
        await using (connection)
        await using (queryDb)
        {
            var loginReq = new LoginRequest
            {
                Email = "nonexistent_hacker@cinemaflow.vn",
                Password = "WrongPassword123!"
            };

            var loginResult = await controller.Login(loginReq);
            var unauthResult = Assert.IsType<UnauthorizedObjectResult>(loginResult.Result);
            var loginResponse = Assert.IsType<ApiResponse<AuthDto>>(unauthResult.Value);
            Assert.False(loginResponse.Success);

            var auditEvent = await queryDb.SecurityAuditEvents
                .FirstOrDefaultAsync(e => e.EventType == "login.failed");

            Assert.NotNull(auditEvent);
            Assert.False(auditEvent.Success);
            Assert.Null(auditEvent.UserId);
            Assert.Contains("nonexistent_hacker@cinemaflow.vn", auditEvent.Detail);
        }
    }

    [Fact]
    public async Task RevokeAllSessions_RevokesAllAccessTokens_AndRecordsSessionRevokeAllAuditEvent()
    {
        var (controller, authService, queryDb, connection) = await CreateTestEnvAsync();
        await using (connection)
        await using (queryDb)
        {
            // Register and login to create tokens
            var regReq = new RegisterRequest
            {
                FullName = "Phiên Đăng Nhập",
                Email = "session_user@cinemaflow.vn",
                Password = "P@sswordSecure2026!",
                ConfirmPassword = "P@sswordSecure2026!"
            };
            var regResult = await controller.Register(regReq);
            var regResponse = (ApiResponse<AuthDto>)((OkObjectResult)regResult.Result!).Value!;
            var token1 = regResponse.Data!.AccessToken;
            var userId = regResponse.Data.User.Id;

            // Second login from another device
            var loginResult = await controller.Login(new LoginRequest
            {
                Email = "session_user@cinemaflow.vn",
                Password = "P@sswordSecure2026!"
            });
            var token2 = ((ApiResponse<AuthDto>)((OkObjectResult)loginResult.Result!).Value!).Data!.AccessToken;

            // Verify both tokens are active before revoke
            Assert.NotNull(await authService.ValidateAccessTokenAsync(token1));
            Assert.NotNull(await authService.ValidateAccessTokenAsync(token2));

            // Set User claims and execute RevokeAllSessions
            var httpContext = new DefaultHttpContext();
            var claims = new[] { new System.Security.Claims.Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

            var revokeResult = await controller.RevokeAllSessions();
            var okRevoke = Assert.IsType<OkObjectResult>(revokeResult.Result);
            var revokeResponse = Assert.IsType<ApiResponse<bool>>(okRevoke.Value);
            Assert.True(revokeResponse.Success);

            // Verify both tokens are invalidated
            Assert.Null(await authService.ValidateAccessTokenAsync(token1));
            Assert.Null(await authService.ValidateAccessTokenAsync(token2));

            // Verify audit event exists
            var auditEvent = await queryDb.SecurityAuditEvents
                .FirstOrDefaultAsync(e => e.EventType == "session.revoke_all");

            Assert.NotNull(auditEvent);
            Assert.True(auditEvent.Success);
            Assert.Equal(userId, auditEvent.UserId);
        }
    }

    [Fact]
    public async Task ChangePassword_WhenSuccessful_RecordsSessionRevokeAllAuditEvent()
    {
        var (controller, authService, queryDb, connection) = await CreateTestEnvAsync();
        await using (connection)
        await using (queryDb)
        {
            var regReq = new RegisterRequest
            {
                FullName = "Đổi Mật Khẩu",
                Email = "change_pwd@cinemaflow.vn",
                Password = "OldPassword2026!",
                ConfirmPassword = "OldPassword2026!"
            };
            var regResult = await controller.Register(regReq);
            var regResponse = (ApiResponse<AuthDto>)((OkObjectResult)regResult.Result!).Value!;
            var userId = regResponse.Data!.User.Id;
            var token = regResponse.Data.AccessToken;

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers["Authorization"] = $"Bearer {token}";
            var claims = new[] { new System.Security.Claims.Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

            var changeResult = await controller.ChangePassword(new ChangePasswordRequest
            {
                CurrentPassword = "OldPassword2026!",
                NewPassword = "NewPassword2026!",
                ConfirmPassword = "NewPassword2026!"
            });

            var okChange = Assert.IsType<OkObjectResult>(changeResult.Result);
            var changeResponse = Assert.IsType<ApiResponse<bool>>(okChange.Value);
            Assert.True(changeResponse.Success);

            var revokeAuditEvent = await queryDb.SecurityAuditEvents
                .FirstOrDefaultAsync(e => e.EventType == "session.revoke_all");

            Assert.NotNull(revokeAuditEvent);
            Assert.True(revokeAuditEvent.Success);
            Assert.Equal(userId, revokeAuditEvent.UserId);

            var pwdAuditEvent = await queryDb.SecurityAuditEvents
                .FirstOrDefaultAsync(e => e.EventType == "password.change");
            Assert.NotNull(pwdAuditEvent);
            Assert.True(pwdAuditEvent.Success);
        }
    }

    [Fact]
    public void ApiRateLimits_GetClientIpAddress_WhenRemoteIpIsExternal_RejectsForgedXffHeader()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.88");
        httpContext.Request.Headers["X-Forwarded-For"] = "203.0.113.1";
        httpContext.Request.Headers["X-Real-IP"] = "203.0.113.2";

        var clientIp = ApiRateLimits.GetClientIpAddress(httpContext);

        // Security check: attacker from 198.51.100.88 CANNOT spoof IP to 203.0.113.1
        Assert.Equal("198.51.100.88", clientIp);
    }

    [Fact]
    public void ApiRateLimits_GetClientIpAddress_WhenRemoteIpIsLoopback_UsesForwardedForIp()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        httpContext.Request.Headers["X-Forwarded-For"] = "203.0.113.1, 10.0.0.1";

        var clientIp = ApiRateLimits.GetClientIpAddress(httpContext);

        // Legitimate reverse proxy on localhost extracts real client IP
        Assert.Equal("203.0.113.1", clientIp);
    }

    [Fact]
    public void ApiRateLimits_AddTrustedProxy_AllowsConfiguredReverseProxyIp()
    {
        const string proxyIp = "10.250.0.1";
        ApiRateLimits.AddTrustedProxy(proxyIp);

        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse(proxyIp);
        httpContext.Request.Headers["X-Forwarded-For"] = "198.51.100.99";

        var clientIp = ApiRateLimits.GetClientIpAddress(httpContext);

        Assert.Equal("198.51.100.99", clientIp);
    }

    [Fact]
    public void DeadCodeAudit_AccountService_IsCompletelyRemoved()
    {
        var serverAssembly = typeof(AuthController).Assembly;
        var accountServiceType = serverAssembly.GetTypes()
            .FirstOrDefault(t => t.Name.Contains("AccountService", StringComparison.OrdinalIgnoreCase));

        Assert.Null(accountServiceType);

        var filePath = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Server", "WebXemPhim.Server", "Services", "AccountService.cs");

        Assert.False(File.Exists(filePath), $"Dead code file {filePath} should not exist.");
    }
}
