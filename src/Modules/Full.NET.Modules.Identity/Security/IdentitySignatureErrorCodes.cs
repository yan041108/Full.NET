using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity.Security;

/// <summary>请求签名认证稳定错误码；与规格文档和本地化资源键一致。</summary>
internal static class IdentitySignatureErrorCodes
{
    public const string MissingHeaders = IdentityErrorCodes.SignatureMissingHeaders;

    public const string InvalidVersion = IdentityErrorCodes.SignatureInvalidVersion;

    public const string InvalidTimestamp = IdentityErrorCodes.SignatureInvalidTimestamp;

    public const string TimestampExpired = IdentityErrorCodes.SignatureTimestampExpired;

    public const string TimestampInFuture = IdentityErrorCodes.SignatureTimestampInFuture;

    public const string InvalidNonce = IdentityErrorCodes.SignatureInvalidNonce;

    public const string ReplayDetected = IdentityErrorCodes.SignatureReplayDetected;

    public const string InvalidEncoding = IdentityErrorCodes.SignatureInvalidEncoding;

    public const string InvalidSignature = IdentityErrorCodes.SignatureInvalidSignature;

    public const string AccessKeyNotFound = IdentityErrorCodes.SignatureAccessKeyNotFound;

    public const string AccessKeyDisabled = IdentityErrorCodes.SignatureAccessKeyDisabled;

    public const string AccessKeyExpired = IdentityErrorCodes.SignatureAccessKeyExpired;

    public const string TenantScopeMismatch = IdentityErrorCodes.SignatureTenantScopeMismatch;
}
