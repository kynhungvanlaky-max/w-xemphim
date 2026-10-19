using WebXemPhim.Shared.Requests;

namespace WebXemPhim.Server.Security;

public enum WebhookVerificationStatus
{
    Valid,
    MissingSignature,
    MalformedPayload,
    InvalidSignature,
    TimestampExpired
}

public sealed record WebhookVerificationResult(bool IsValid, WebhookVerificationStatus Status, string? ErrorMessage = null)
{
    public static WebhookVerificationResult Success() =>
        new(true, WebhookVerificationStatus.Valid);

    public static WebhookVerificationResult Fail(WebhookVerificationStatus status, string message) =>
        new(false, status, message);
}

public interface IPaymentWebhookVerifier
{
    /// <summary>
    /// Builds canonical string from payment webhook request
    /// </summary>
    string BuildCanonicalPayload(PaymentWebhookRequest request);

    /// <summary>
    /// Computes HMAC-SHA512 hex signature for the request
    /// </summary>
    string ComputeSignature(PaymentWebhookRequest request, string? secretKey = null);

    /// <summary>
    /// Verifies the signature using constant-time comparison
    /// </summary>
    WebhookVerificationResult Verify(PaymentWebhookRequest request, string? overrideSecretKey = null);

    /// <summary>
    /// Shortcut boolean verification
    /// </summary>
    bool VerifySignature(PaymentWebhookRequest request, string? overrideSecretKey = null);

    /// <summary>
    /// Resolves configured secret key for gateway
    /// </summary>
    string GetSecretKeyForGateway(string? gateway);
}
