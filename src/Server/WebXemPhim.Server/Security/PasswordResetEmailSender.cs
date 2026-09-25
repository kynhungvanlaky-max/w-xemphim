using System.Net;
using System.Net.Mail;

namespace WebXemPhim.Server.Security;

public interface IPasswordResetEmailSender
{
    bool IsConfigured { get; }
    Task SendAsync(string email, string token);
}

public sealed class PasswordResetEmailSender(IConfiguration config) : IPasswordResetEmailSender
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(config["Security:Smtp:Host"])
        && !string.IsNullOrWhiteSpace(config["Security:Smtp:From"])
        && Uri.TryCreate(config["Security:PasswordResetClientUrl"], UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo)
        && uri.AbsolutePath == "/" && uri.Query.Length == 0 && uri.Fragment.Length == 0;

    public async Task SendAsync(string email, string token)
    {
        if (!IsConfigured) throw new InvalidOperationException("Password reset email is not configured.");
        var url = config["Security:PasswordResetClientUrl"]!.TrimEnd('/') + "/reset-password?token=" + Uri.EscapeDataString(token);
        using var message = new MailMessage(config["Security:Smtp:From"]!, email)
        {
            Subject = "CinemaFlow - Đặt lại mật khẩu",
            Body = $"Bạn đã yêu cầu đặt lại mật khẩu. Liên kết có hiệu lực trong 15 phút và chỉ dùng một lần:\n{url}\nNếu bạn không yêu cầu, hãy bỏ qua email này.",
            IsBodyHtml = false
        };
        using var smtp = new SmtpClient(config["Security:Smtp:Host"], config.GetValue("Security:Smtp:Port", 587))
        {
            EnableSsl = true,
            UseDefaultCredentials = false,
            Timeout = 10000
        };
        if (!string.IsNullOrWhiteSpace(config["Security:Smtp:Username"]))
            smtp.Credentials = new NetworkCredential(config["Security:Smtp:Username"], config["Security:Smtp:Password"]);
        await smtp.SendMailAsync(message);
    }
}
