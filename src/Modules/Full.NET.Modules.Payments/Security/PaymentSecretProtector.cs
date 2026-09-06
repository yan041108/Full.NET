using Microsoft.AspNetCore.DataProtection;

namespace Full.NET.Modules.Payments.Security;

/// <summary>使用 Data Protection 保护支付 API 密钥与商户私钥，避免明文落库。</summary>
internal sealed class PaymentSecretProtector(IDataProtectionProvider dataProtectionProvider)
{
    private const string ApiV3KeyPurpose = "Full.NET.Payments.MerchantConfigApiV3Key.v1";
    private const string PrivateKeyPurpose = "Full.NET.Payments.MerchantConfigPrivateKey.v1";

    private readonly IDataProtector _apiV3KeyProtector =
        dataProtectionProvider.CreateProtector(ApiV3KeyPurpose);

    private readonly IDataProtector _privateKeyProtector =
        dataProtectionProvider.CreateProtector(PrivateKeyPurpose);

    /// <summary>保护 API v3 密钥。</summary>
    /// <param name="apiV3Key">明文密钥。</param>
    /// <returns>受保护密文。</returns>
    public string ProtectApiV3Key(string apiV3Key) =>
        _apiV3KeyProtector.Protect(apiV3Key);

    /// <summary>解保护 API v3 密钥；仅供渠道调用使用。</summary>
    /// <param name="apiV3KeyProtected">受保护密文。</param>
    /// <returns>明文密钥。</returns>
    public string UnprotectApiV3Key(string apiV3KeyProtected) =>
        _apiV3KeyProtector.Unprotect(apiV3KeyProtected);

    /// <summary>保护商户私钥 PEM。</summary>
    /// <param name="privateKeyPem">明文私钥。</param>
    /// <returns>受保护密文。</returns>
    public string ProtectPrivateKey(string privateKeyPem) =>
        _privateKeyProtector.Protect(privateKeyPem);

    /// <summary>解保护商户私钥 PEM；仅供渠道签名使用。</summary>
    /// <param name="privateKeyProtected">受保护密文。</param>
    /// <returns>明文私钥。</returns>
    public string UnprotectPrivateKey(string privateKeyProtected) =>
        _privateKeyProtector.Unprotect(privateKeyProtected);
}
