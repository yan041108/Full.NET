namespace Full.NET.Modules.Identity.Security;

/// <summary>请求签名认证配置；时钟偏差与 Nonce 保留期可配置但有硬边界。</summary>
internal sealed class SignatureAuthenticationOptions
{
    public const string SectionName = "Identity:Signature";

    /// <summary>签名协议版本；仅支持 <c>1</c>。</summary>
    public const string SupportedVersion = "1";

    public const string AccessKeyIdHeader = "X-FullNET-Access-Key-Id";

    public const string TimestampHeader = "X-FullNET-Timestamp";

    public const string NonceHeader = "X-FullNET-Nonce";

    public const string SignatureHeader = "X-FullNET-Signature";

    public const string SignatureVersionHeader = "X-FullNET-Signature-Version";

    public const string TenantIdHeader = "X-FullNET-Tenant-Id";

    /// <summary>允许的时间戳偏差（秒）；默认 300，范围 30–900。</summary>
    public int ClockSkewSeconds { get; set; } = 300;

    /// <summary>Nonce 记录在时间戳窗口之外的额外保留秒数。</summary>
    public int NonceRetentionSeconds { get; set; } = 300;

    /// <summary>签名验签允许读取的最大请求体字节数；默认 1 MiB。</summary>
    public int MaxBodyBytes { get; set; } = 1_048_576;

    public int MinNonceLength { get; set; } = 16;

    public int MaxNonceLength { get; set; } = 64;

    /// <summary>Access Key 公开标识最大长度，与 KeyPrefix 列一致。</summary>
    public int MaxAccessKeyIdLength { get; set; } = 16;

    public const int MinBodyBytesLimit = 1;

    public const int MaxBodyBytesLimit = 10_485_760;

    public int MinClockSkewSeconds => 30;

    public int MaxClockSkewSeconds => 900;
}
