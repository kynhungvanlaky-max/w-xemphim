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

    private Guid CurrentUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
