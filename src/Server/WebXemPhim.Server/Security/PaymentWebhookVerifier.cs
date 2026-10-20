using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using WebXemPhim.Shared.Requests;

namespace WebXemPhim.Server.Security;

public sealed class PaymentWebhookVerifier : IPaymentWebhookVerifier
{
    public const string DefaultWebhookSecret = "CinemaFlow_Secure_Payment_Webhook_HMAC_SHA512_Secret_Key_2026_AtLeast32Bytes!";
    public const string DefaultPayOSSecret = "CinemaFlow_PayOS_Webhook_Secret_Key_2026_AtLeast32Bytes!";
    public const string DefaultVNPaySecret = "CinemaFlow_VNPay_Hash_Secret_Key_2026_AtLeast32Bytes!";

    private readonly IConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly IHostEnvironment? _environment;

    public PaymentWebhookVerifier(
        IConfiguration configuration,
        TimeProvider? timeProvider = null,
        IHostEnvironment? environment = null)
    {
        _configuration = configuration;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _environment = environment;
    }

    public string GetSecretKeyForGateway(string? gateway)
    {
        if (!string.IsNullOrWhiteSpace(gateway))
        {
            string? rawGatewayKey = null;
            var gwWebhook = _configuration[$"Payment:Gateways:{gateway}:WebhookSecret"];
            if (!string.IsNullOrWhiteSpace(gwWebhook))
            {
                rawGatewayKey = gwWebhook;
            }
            else
            {
                var gwHash = _configuration[$"Payment:Gateways:{gateway}:HashSecret"];
                if (!string.IsNullOrWhiteSpace(gwHash))
                {
                    rawGatewayKey = gwHash;
                }
                else if (gateway.Equals("PayOS", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(_configuration["PAYOS_WEBHOOK_SECRET"]))
                {
                    rawGatewayKey = _configuration["PAYOS_WEBHOOK_SECRET"];
                }
                else if (gateway.Equals("VNPay", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(_configuration["VNPAY_HASH_SECRET"]))
                {
                    rawGatewayKey = _configuration["VNPAY_HASH_SECRET"];
                }
            }

            var gatewayKey = rawGatewayKey?.Trim();

            if (!string.IsNullOrWhiteSpace(gatewayKey))
            {
                if (_environment?.IsProduction() == true)
                {
                    if (string.Equals(gatewayKey, DefaultWebhookSecret, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(gatewayKey, DefaultPayOSSecret, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(gatewayKey, DefaultVNPaySecret, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"Production gateway secret key for '{gateway}' cannot be the default placeholder key.");
                    }
                    if (gatewayKey.Length < 32)
                    {
                        throw new InvalidOperationException($"Production gateway secret key for '{gateway}' must be at least 32 characters long.");
                    }
                }
                return gatewayKey;
            }
        }

        string? rawMasterKey = null;
        var payWebhook = _configuration["Payment:WebhookSecret"];
        if (!string.IsNullOrWhiteSpace(payWebhook))
        {
            rawMasterKey = payWebhook;
        }
        else
        {
            var secPayWebhook = _configuration["Security:PaymentWebhookSecret"];
            if (!string.IsNullOrWhiteSpace(secPayWebhook))
            {
                rawMasterKey = secPayWebhook;
            }
            else if (!string.IsNullOrWhiteSpace(_configuration["PAYMENT_WEBHOOK_SECRET"]))
            {
                rawMasterKey = _configuration["PAYMENT_WEBHOOK_SECRET"];
            }
        }

        var masterKey = rawMasterKey?.Trim();

        if (!string.IsNullOrWhiteSpace(masterKey))
        {
            if (_environment?.IsProduction() == true)
            {
                if (string.Equals(masterKey, DefaultWebhookSecret, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Production payment webhook secret cannot be the default placeholder key.");
                }
                if (masterKey.Length < 32)
                {
                    throw new InvalidOperationException("Production payment webhook secret must be at least 32 characters long.");
                }
            }
            return masterKey;
        }

        if (_environment?.IsProduction() == true)
        {
            throw new InvalidOperationException("Payment webhook secret key is not configured in Production.");
        }

        return DefaultWebhookSecret;
    }

    public string BuildCanonicalPayload(PaymentWebhookRequest request)
    {
        var orderCode = request.OrderCode?.Trim() ?? string.Empty;
        var amount = request.Amount.ToString("G29", CultureInfo.InvariantCulture);
        var status = request.Status?.Trim() ?? string.Empty;
        var transRef = request.TransactionReference?.Trim() ?? string.Empty;
        var gateway = request.Gateway?.Trim() ?? string.Empty;
        var timestamp = request.Timestamp.HasValue ? request.Timestamp.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;

        return $"amount={amount}&gateway={gateway}&orderCode={orderCode}&status={status}&timestamp={timestamp}&transactionRef={transRef}";
    }

    public string ComputeSignature(PaymentWebhookRequest request, string? secretKey = null)
    {
        var key = !string.IsNullOrWhiteSpace(secretKey) ? secretKey.Trim() : GetSecretKeyForGateway(request.Gateway);
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var payload = BuildCanonicalPayload(request);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);

        using var hmac = new HMACSHA512(keyBytes);
        var hashBytes = hmac.ComputeHash(payloadBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public WebhookVerificationResult Verify(PaymentWebhookRequest request, string? overrideSecretKey = null)
    {
        if (request is null)
        {
            return WebhookVerificationResult.Fail(WebhookVerificationStatus.MalformedPayload, "Dữ liệu webhook không được để trống.");
        }

        if (string.IsNullOrWhiteSpace(request.Signature))
        {
            return WebhookVerificationResult.Fail(WebhookVerificationStatus.MissingSignature, "Chữ ký webhook không được để trống.");
        }

        if (string.IsNullOrWhiteSpace(request.OrderCode))
        {
            return WebhookVerificationResult.Fail(WebhookVerificationStatus.MalformedPayload, "Mã đơn hàng không được để trống.");
        }

        var expectedSignature = ComputeSignature(request, overrideSecretKey);

        var actualSigBytes = Encoding.UTF8.GetBytes(request.Signature.Trim().ToLowerInvariant());
        var expectedSigBytes = Encoding.UTF8.GetBytes(expectedSignature.ToLowerInvariant());

        if (actualSigBytes.Length != expectedSigBytes.Length ||
            !CryptographicOperations.FixedTimeEquals(actualSigBytes, expectedSigBytes))
        {
            return WebhookVerificationResult.Fail(WebhookVerificationStatus.InvalidSignature, "Chữ ký xác thực webhook không hợp lệ (403 Forbidden).");
        }

        return WebhookVerificationResult.Success();
    }

    public bool VerifySignature(PaymentWebhookRequest request, string? overrideSecretKey = null) =>
        Verify(request, overrideSecretKey).IsValid;
}
