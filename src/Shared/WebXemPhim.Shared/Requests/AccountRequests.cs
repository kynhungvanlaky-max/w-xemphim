using System.ComponentModel.DataAnnotations;
using WebXemPhim.Shared.Enums;

namespace WebXemPhim.Shared.Requests;

public class RegisterRequest
{
    [Required(ErrorMessage = "Vui lòng nhập họ và tên."), StringLength(80, MinimumLength = 2, ErrorMessage = "Họ và tên cần từ 2 đến 80 ký tự.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập email."), EmailAddress(ErrorMessage = "Email chưa đúng định dạng."), StringLength(160, ErrorMessage = "Email không được vượt quá 160 ký tự.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu."), StringLength(100, MinimumLength = 8, ErrorMessage = "Mật khẩu cần từ 8 đến 100 ký tự.")]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).+$", ErrorMessage = "Mật khẩu phải có chữ hoa, chữ thường và chữ số.")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập lại mật khẩu."), Compare(nameof(Password), ErrorMessage = "Mật khẩu nhập lại chưa khớp.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Range(typeof(bool), "true", "true", ErrorMessage = "Bạn cần đồng ý với điều khoản để đăng ký.")]
    public bool AcceptTerms { get; set; }
}

public class LoginRequest
{
    [Required(ErrorMessage = "Vui lòng nhập email hoặc tên đăng nhập."), StringLength(160)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu."), StringLength(100)]
    public string Password { get; set; } = string.Empty;
}

public sealed class VerifyMfaLoginRequest
{
    [Required, StringLength(64, MinimumLength = 64), RegularExpression("^[a-fA-F0-9]{64}$")]
    public string ChallengeToken { get; set; } = string.Empty;

    [Required, StringLength(24, MinimumLength = 6)]
    public string Code { get; set; } = string.Empty;
}

public sealed class BeginMfaSetupRequest
{
    [Required, StringLength(100)]
    public string CurrentPassword { get; set; } = string.Empty;
}

public sealed class EnableMfaRequest
{
    [Required, RegularExpression("^[0-9]{6}$")]
    public string Code { get; set; } = string.Empty;
}

public sealed class DisableMfaRequest
{
    [Required, StringLength(100)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required, StringLength(24, MinimumLength = 6)]
    public string Code { get; set; } = string.Empty;
}

public class ForgotPasswordRequest
{
    [Required, EmailAddress, StringLength(160)]
    public string Email { get; set; } = string.Empty;
}

public class ResetPasswordRequest
{
    [StringLength(160)] // Legacy optional field; possession of the reset token is the proof.
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(64, MinimumLength = 64), RegularExpression("^[a-fA-F0-9]{64}$")]
    public string Token { get; set; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 8)]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).+$", ErrorMessage = "Mật khẩu phải có chữ hoa, chữ thường và chữ số.")]
    public string NewPassword { get; set; } = string.Empty;

    [Required, Compare(nameof(NewPassword))]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class SubscribeVipRequest
{
    [Required, StringLength(32)]
    public string PlanCode { get; set; } = string.Empty;

    [EnumDataType(typeof(BillingCycle))]
    public BillingCycle BillingCycle { get; set; }
}

public class CreatePaymentRequest
{
    [Required, StringLength(32)]
    public string PlanId { get; set; } = string.Empty;

    [EnumDataType(typeof(PaymentMethod))]
    public PaymentMethod Method { get; set; }

    [StringLength(32)]
    public string? CouponCode { get; set; }
}

public class ApplyCouponRequest
{
    [Required, StringLength(32)]
    public string CouponCode { get; set; } = string.Empty;

    [Required, StringLength(32)]
    public string PlanId { get; set; } = string.Empty;
}

public class CouponValidationResultDto
{
    public bool IsValid { get; set; }
    public string CouponCode { get; set; } = string.Empty;
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal OriginalPrice { get; set; }
    public decimal FinalPrice { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class UpsertReviewRequest
{
    [Range(1, 10)]
    public int Rating { get; set; }

    [StringLength(800)]
    public string Comment { get; set; } = string.Empty;
}

public class ChangePasswordRequest
{
    [Required(ErrorMessage = "Vui lòng nhập mật khẩu hiện tại."), StringLength(100)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu mới."), StringLength(100, MinimumLength = 8, ErrorMessage = "Mật khẩu mới cần từ 8 đến 100 ký tự.")]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).+$", ErrorMessage = "Mật khẩu phải có chữ hoa, chữ thường và chữ số.")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng xác nhận mật khẩu mới."), Compare(nameof(NewPassword), ErrorMessage = "Mật khẩu xác nhận không khớp.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

