using WebXemPhim.Shared.Models;

namespace WebXemPhim.Server.Services;

public enum PaymentWebhookProcessStatus
{
    Success,
    AlreadyProcessed,
    InvalidSignature,
    OrderNotFound,
    AmountMismatch,
    DuplicateTransactionReference,
    PaymentFailedByGateway
}

public sealed record PaymentWebhookResult(
    PaymentWebhookProcessStatus Status,
    PaymentDto? Payment,
    string Message)
{
    public static PaymentWebhookResult Ok(PaymentDto payment, string message = "Xác nhận thanh toán thành công.") =>
        new(PaymentWebhookProcessStatus.Success, payment, message);

    public static PaymentWebhookResult AlreadyCompleted(PaymentDto payment, string message = "Giao dịch đã được xử lý trước đó.") =>
        new(PaymentWebhookProcessStatus.AlreadyProcessed, payment, message);

    public static PaymentWebhookResult InvalidSig(string message = "Chữ ký bảo mật webhook không hợp lệ (403 Forbidden).") =>
        new(PaymentWebhookProcessStatus.InvalidSignature, null, message);

    public static PaymentWebhookResult NotFound(string message = "Không tìm thấy đơn thanh toán.") =>
        new(PaymentWebhookProcessStatus.OrderNotFound, null, message);

    public static PaymentWebhookResult AmountMismatch(string message = "Số tiền thanh toán không khớp với giá trị đơn hàng.") =>
        new(PaymentWebhookProcessStatus.AmountMismatch, null, message);

    public static PaymentWebhookResult DuplicateTransaction(string message = "Mã giao dịch ngân hàng đã được sử dụng cho đơn hàng khác.") =>
        new(PaymentWebhookProcessStatus.DuplicateTransactionReference, null, message);

    public static PaymentWebhookResult GatewayFailed(PaymentDto payment, string message) =>
        new(PaymentWebhookProcessStatus.PaymentFailedByGateway, payment, message);
}
