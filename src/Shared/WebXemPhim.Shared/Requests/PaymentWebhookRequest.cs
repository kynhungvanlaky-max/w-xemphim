using System.ComponentModel.DataAnnotations;

namespace WebXemPhim.Shared.Requests;

/// <summary>
/// Webhook payload request DTO from payment gateways (PayOS, VNPay, MoMo, etc.)
/// </summary>
public sealed class PaymentWebhookRequest
{
    [Required(ErrorMessage = "Mã đơn hàng không được để trống.")]
    [StringLength(64, MinimumLength = 1, ErrorMessage = "Mã đơn hàng không hợp lệ.")]
    public string OrderCode { get; set; } = string.Empty;

    [Range(0.01, 100_000_000, ErrorMessage = "Số tiền thanh toán phải lớn hơn 0.")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "Trạng thái thanh toán không được để trống.")]
    [StringLength(32, ErrorMessage = "Trạng thái thanh toán vượt quá độ dài.")]
    public string Status { get; set; } = string.Empty;

    [StringLength(120, ErrorMessage = "Mã giao dịch cổng thanh toán vượt quá độ dài.")]
    public string? TransactionReference { get; set; }

    [StringLength(32, ErrorMessage = "Tên cổng thanh toán vượt quá độ dài.")]
    public string? Gateway { get; set; }

    public long? Timestamp { get; set; }

    [Required(ErrorMessage = "Chữ ký xác thực webhook không được để trống.")]
    [StringLength(256, MinimumLength = 32, ErrorMessage = "Chữ ký xác thực không hợp lệ.")]
    public string Signature { get; set; } = string.Empty;
}
