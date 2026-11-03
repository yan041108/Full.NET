namespace Full.NET.Modules.Identity.Configuration;

/// <summary>同源后台 BFF 配置；状态保护与协议中心密钥分开，生产由密钥管理注入。</summary>
internal sealed class IdentityBrowserOptions
{
    public const string SectionName = "Identity:Browser";
    public bool Enable { get; set; }
    public string PublicOrigin { get; set; } = string.Empty;
    public string ClientId { get; set; } = "fullnet-admin-bff";
    public string ClientSecret { get; set; } = string.Empty;
    public string StateSigningKeyBase64 { get; set; } = string.Empty;
    public string StateEncryptionKeyBase64 { get; set; } = string.Empty;
    public int SessionHours { get; set; } = 8;
}
