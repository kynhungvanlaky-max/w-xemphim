using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Server.Controllers;

[ApiController]
[Route("api/payments")]
public class PaymentsController(IPaymentService paymentService) : ControllerBase
{
    [HttpGet("plans")]
    public async Task<ActionResult<ApiResponse<List<VipPlanDto>>>> GetPlans() =>
        Ok(ApiResponse<List<VipPlanDto>>.Ok(await paymentService.GetPlansAsync()));

    [HttpPost("apply-coupon")]
    [EnableRateLimiting("CouponValidation")]
    public async Task<ActionResult<ApiResponse<CouponValidationResultDto>>> ApplyCoupon([FromBody] ApplyCouponRequest request)
    {
        var result = await paymentService.ValidateCouponAsync(request);
        return Ok(ApiResponse<CouponValidationResultDto>.Ok(result, result.Message));
    }

    [Authorize]
    [HttpPost("checkout")]
    [EnableRateLimiting("Checkout")]
    public async Task<ActionResult<ApiResponse<PaymentDto>>> Checkout(CreatePaymentRequest request)
    {
        var payment = await paymentService.CreatePaymentAsync(CurrentUserId(), request);
        if (payment is null)
            return BadRequest(ApiResponse<PaymentDto>.Fail("Gói VIP hoặc phương thức thanh toán không hợp lệ."));

        return Ok(ApiResponse<PaymentDto>.Ok(payment, payment.Status == WebXemPhim.Shared.Enums.PaymentStatus.Completed
            ? "VIP thử nghiệm đã được kích hoạt. Không có tiền thật được thu."
            : "Đơn đang chờ xác nhận thanh toán. VIP chưa được kích hoạt; cổng thanh toán thật chưa được tích hợp."));
    }

    [Authorize]
    [HttpGet("history")]
    public async Task<ActionResult<ApiResponse<List<PaymentDto>>>> History() =>
        Ok(ApiResponse<List<PaymentDto>>.Ok(await paymentService.GetHistoryAsync(CurrentUserId())));

    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> ProcessWebhook([FromBody] PaymentWebhookRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.OrderCode) || string.IsNullOrWhiteSpace(request.Signature))
        {
            return BadRequest(ApiResponse<PaymentDto>.Fail("Dữ liệu webhook không hợp lệ hoặc thiếu thông tin bắt buộc."));
        }

        if (request.Amount <= 0)
        {
            return BadRequest(ApiResponse<PaymentDto>.Fail("Số tiền thanh toán phải lớn hơn 0."));
        }

        if (string.IsNullOrWhiteSpace(request.Status))
        {
            return BadRequest(ApiResponse<PaymentDto>.Fail("Trạng thái giao dịch không được để trống."));
        }

        var result = await paymentService.ProcessWebhookAsync(request);

        return result.Status switch
        {
            PaymentWebhookProcessStatus.Success =>
                Ok(ApiResponse<PaymentDto>.Ok(result.Payment!, result.Message)),

            PaymentWebhookProcessStatus.AlreadyProcessed =>
                Ok(ApiResponse<PaymentDto>.Ok(result.Payment!, result.Message)),

            PaymentWebhookProcessStatus.InvalidSignature =>
                StatusCode(StatusCodes.Status403Forbidden, ApiResponse<PaymentDto>.Fail(result.Message)),

            PaymentWebhookProcessStatus.OrderNotFound =>
                NotFound(ApiResponse<PaymentDto>.Fail(result.Message)),

            PaymentWebhookProcessStatus.AmountMismatch or
            PaymentWebhookProcessStatus.DuplicateTransactionReference =>
                BadRequest(ApiResponse<PaymentDto>.Fail(result.Message)),

            PaymentWebhookProcessStatus.PaymentFailedByGateway =>
                Ok(ApiResponse<PaymentDto>.Ok(result.Payment!, result.Message)),

            _ => BadRequest(ApiResponse<PaymentDto>.Fail("Yêu cầu xử lý webhook không hợp lệ."))
        };
    }

    private Guid CurrentUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
