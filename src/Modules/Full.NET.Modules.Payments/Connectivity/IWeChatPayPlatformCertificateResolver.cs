namespace Full.NET.Modules.Payments.Connectivity;

/// <summary>按序列号解析微信支付平台证书公钥。</summary>
internal interface IWeChatPayPlatformCertificateResolver
{
    /// <summary>获取指定序列号的平台公钥 PEM。</summary>
    /// <param name="merchantConfig">商户配置。</param>
    /// <param name="platformSerialNo">平台证书序列号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>公钥 PEM；无法解析时返回 null。</returns>
    Task<string?> ResolvePublicKeyPemAsync(
        Persistence.PaymentMerchantConfigRecord merchantConfig,
        string platformSerialNo,
        CancellationToken cancellationToken = default);
}
