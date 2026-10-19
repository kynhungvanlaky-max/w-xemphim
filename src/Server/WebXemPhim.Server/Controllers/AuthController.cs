using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using WebXemPhim.Server.Security;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Server.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    IAuthService authService,
    IPasswordResetEmailSender emailSender,
    ISecurityAuditService audit,
    ILogger<AuthController> logger,
    IStreamSessionService? streamSessionService = null) : ControllerBase
{
    [EnableRateLimiting("Authentication")]
    [HttpPost("register")]
    public async Task<ActionResult<ApiResponse<AuthDto>>> Register(RegisterRequest request)
    {
        var result = await authService.RegisterAsync(request);
        if (result.Failure == AuthFailure.DuplicateEmail)
            return Conflict(ApiResponse<AuthDto>.Fail("Email này đã được đăng ký."));
        if (result.Failure == AuthFailure.WeakPassword)
            return BadRequest(ApiResponse<AuthDto>.Fail("Mật khẩu này quá phổ biến hoặc dễ đoán. Vui lòng chọn mật khẩu mạnh hơn để bảo vệ tài khoản."));

        await Audit(result.Data!.User.Id, "register.success", true, $"Email: {request.Email.Trim()}");
        return Ok(ApiResponse<AuthDto>.Ok(result.Data!, "Tạo tài khoản thành công."));
    }

    [HttpPost("login")]
    [EnableRateLimiting("Authentication")]
    public async Task<ActionResult<ApiResponse<AuthDto>>> Login(LoginRequest request)
    {
        var result = await authService.LoginAsync(request);
        if (result.Data is null)
        {
            await Audit(null, "login.failed", false, $"Email: {request.Email.Trim()}");
            return Unauthorized(ApiResponse<AuthDto>.Fail("Email hoặc mật khẩu không chính xác."));
        }

        await Audit(result.Data.User.Id, "login.success", true, $"Email: {result.Data.User.Email}");
        return Ok(ApiResponse<AuthDto>.Ok(result.Data, result.Data.RequiresMfa ? "Vui lòng nhập mã xác thực hai lớp." : "Đăng nhập thành công."));
    }

    [HttpPost("mfa/verify-login")]
    [EnableRateLimiting("Authentication")]
    public async Task<ActionResult<ApiResponse<AuthDto>>> VerifyMfaLogin(VerifyMfaLoginRequest request)
    {
        var result = await authService.VerifyMfaLoginAsync(request);
        if (result.Data is null)
        {
            await Audit(null, "mfa.login", false, "Invalid or expired MFA challenge.");
            return Unauthorized(ApiResponse<AuthDto>.Fail("Mã xác thực không chính xác hoặc phiên xác thực đã hết hạn."));
        }
        await Audit(result.Data.User.Id, "mfa.login", true);
        return Ok(ApiResponse<AuthDto>.Ok(result.Data, "Xác thực hai lớp thành công."));
    }

    [HttpPost("forgot-password")]
    [EnableRateLimiting("Authentication")]
    public async Task<ActionResult<ApiResponse<PasswordResetDto?>>> ForgotPassword(ForgotPasswordRequest request)
    {
        if (!emailSender.IsConfigured)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiResponse<PasswordResetDto?>.Fail("Khôi phục mật khẩu chưa khả dụng. Vui lòng liên hệ hỗ trợ."));
        var result = await authService.CreatePasswordResetAsync(request.Email);
        if (result.Data is not null)
        {
            try { await emailSender.SendAsync(request.Email.Trim(), result.Data.ResetToken); }
            catch (Exception) { logger.LogError("Password reset delivery failed; inspect the mail service configuration."); }
        }
        return Ok(ApiResponse<PasswordResetDto?>.Ok(
            null,
            "Nếu email tồn tại, bạn sẽ nhận được hướng dẫn đặt lại mật khẩu."));
    }

    [HttpPost("reset-password")]
    [EnableRateLimiting("Authentication")]
    public async Task<ActionResult<ApiResponse<bool>>> ResetPassword(ResetPasswordRequest request)
    {
        if (!await authService.ResetPasswordAsync(request))
            return BadRequest(ApiResponse<bool>.Fail("Liên kết đặt lại mật khẩu không hợp lệ hoặc đã hết hạn."));

        return Ok(ApiResponse<bool>.Ok(true, "Mật khẩu đã được cập nhật."));
    }

    [Authorize]
    [HttpPost("change-password")]
    [EnableRateLimiting("Authentication")]
    public async Task<ActionResult<ApiResponse<bool>>> ChangePassword(ChangePasswordRequest request)
    {
        var success = await authService.ChangePasswordAsync(CurrentUserId, request, BearerToken);
        await Audit(CurrentUserId, "password.change", success);
        if (success)
        {
            await Audit(CurrentUserId, "session.revoke_all", true, "Password changed");
        }
        return success
            ? Ok(ApiResponse<bool>.Ok(true, "Mật khẩu đã được thay đổi thành công. Các phiên đăng nhập trên thiết bị khác đã được đăng xuất để bảo vệ bạn."))
            : BadRequest(ApiResponse<bool>.Fail("Mật khẩu hiện tại không chính xác hoặc mật khẩu mới không hợp lệ."));
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<ActionResult<ApiResponse<bool>>> Logout()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (!string.IsNullOrEmpty(BearerToken))
        {
            await authService.RevokeAccessTokenAsync(BearerToken);
        }

        var streamService = streamSessionService ?? HttpContext?.RequestServices?.GetService<IStreamSessionService>();
        if (streamService is not null)
        {
            try
            {
                await streamService.RevokeUserStreamsAsync(userId, "Tài khoản đã đăng xuất.");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to revoke active stream leases for user {UserId} on logout.", userId);
            }
        }

        await Audit(userId, "session.logout", true);
        return Ok(ApiResponse<bool>.Ok(true, "Đã kết thúc phiên đăng nhập."));
    }

    [Authorize]
    [HttpPost("revoke-all-sessions")]
    [HttpPost("sessions/revoke-all")]
    public async Task<ActionResult<ApiResponse<bool>>> RevokeAllSessions()
    {
        var userId = CurrentUserId;
        var streamService = streamSessionService ?? HttpContext?.RequestServices?.GetService<IStreamSessionService>();
        if (streamService is not null)
        {
            try
            {
                await streamService.RevokeUserStreamsAsync(userId, "Tất cả phiên đăng nhập đã bị thu hồi.");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to revoke active stream leases for user {UserId} on revoke all sessions.", userId);
            }
        }

        await authService.RevokeAllAccessTokensAsync(userId);
        await Audit(userId, "session.revoke_all", true);
        return Ok(ApiResponse<bool>.Ok(true, "Tất cả các phiên đăng nhập đã được thu hồi."));
    }

    [Authorize]
    [HttpGet("mfa")]
    public async Task<ActionResult<ApiResponse<MfaStatusDto>>> GetMfaStatus() =>
        Ok(ApiResponse<MfaStatusDto>.Ok(await authService.GetMfaStatusAsync(CurrentUserId)));

    [Authorize]
    [HttpPost("mfa/setup")]
    [EnableRateLimiting("Authentication")]
    public async Task<ActionResult<ApiResponse<MfaSetupDto>>> BeginMfaSetup(BeginMfaSetupRequest request)
    {
        var setup = await authService.BeginMfaSetupAsync(CurrentUserId, request.CurrentPassword);
        await Audit(CurrentUserId, "mfa.setup", setup is not null);
        return setup is null
            ? BadRequest(ApiResponse<MfaSetupDto>.Fail("Mật khẩu không chính xác hoặc MFA đã được bật."))
            : Ok(ApiResponse<MfaSetupDto>.Ok(setup, "Đã tạo khóa xác thực. Hãy xác nhận bằng mã 6 số."));
    }

    [Authorize]
    [HttpPost("mfa/enable")]
    [EnableRateLimiting("Authentication")]
    public async Task<ActionResult<ApiResponse<MfaRecoveryCodesDto>>> EnableMfa(EnableMfaRequest request)
    {
        var recoveryCodes = await authService.EnableMfaAsync(CurrentUserId, request.Code, BearerToken);
        await Audit(CurrentUserId, "mfa.enable", recoveryCodes is not null);
        return recoveryCodes is null
            ? BadRequest(ApiResponse<MfaRecoveryCodesDto>.Fail("Mã xác thực không chính xác hoặc thiết lập đã hết hiệu lực."))
            : Ok(ApiResponse<MfaRecoveryCodesDto>.Ok(recoveryCodes, "Đã bật xác thực hai lớp."));
    }

    [Authorize]
    [HttpPost("mfa/disable")]
    [EnableRateLimiting("Authentication")]
    public async Task<ActionResult<ApiResponse<bool>>> DisableMfa(DisableMfaRequest request)
    {
        var disabled = await authService.DisableMfaAsync(CurrentUserId, request.CurrentPassword, request.Code);
        await Audit(CurrentUserId, "mfa.disable", disabled);
        return disabled
            ? Ok(ApiResponse<bool>.Ok(true, "Đã tắt xác thực hai lớp."))
            : BadRequest(ApiResponse<bool>.Fail("Mật khẩu hoặc mã xác thực không chính xác."));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<ApiResponse<UserDto>>> Me()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await authService.GetUserAsync(userId);
        return user is null
            ? Unauthorized(ApiResponse<UserDto>.Fail("Phiên đăng nhập không còn hợp lệ."))
            : Ok(ApiResponse<UserDto>.Ok(user));
    }

    [Authorize]
    [HttpGet("settings")]
    public async Task<ActionResult<ApiResponse<UserSettingsDto>>> GetSettings()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var settings = await authService.GetSettingsAsync(userId);
        return settings is null
            ? NotFound(ApiResponse<UserSettingsDto>.Fail("Không tìm thấy thông tin người dùng."))
            : Ok(ApiResponse<UserSettingsDto>.Ok(settings));
    }

    [Authorize]
    [HttpPut("settings")]
    public async Task<ActionResult<ApiResponse<bool>>> UpdateSettings(UserSettingsDto request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var success = await authService.UpdateSettingsAsync(userId, request);
        return success
            ? Ok(ApiResponse<bool>.Ok(true, "Cập nhật cài đặt thành công."))
            : BadRequest(ApiResponse<bool>.Fail("Không thể cập nhật cài đặt lúc này."));
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string BearerToken => Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        ? Request.Headers.Authorization.ToString()["Bearer ".Length..].Trim() : string.Empty;

    private async Task Audit(Guid? userId, string eventType, bool success, string detail = "")
    {
        try { await audit.RecordAsync(userId, eventType, success, detail); }
        catch (Exception exception) { logger.LogWarning(exception, "Could not persist security audit event {EventType}.", eventType); }
    }
}
