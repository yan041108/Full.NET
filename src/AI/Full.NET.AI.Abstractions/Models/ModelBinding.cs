namespace Full.NET.AI.Abstractions.Models;
/// <summary>由业务授权后签发的模型绑定；只携带短期不透明凭据引用，不含明文或密文。</summary>
/// <param name="ConfigId">配置标识。</param><param name="Version">配置版本。</param>
/// <param name="ProviderKey">静态提供程序键。</param><param name="ModelId">模型标识。</param>
/// <param name="Endpoint">已配置的供应商地址。</param><param name="CredentialReference">请求作用域内的短期引用，不可持久化或跨作用域使用。</param>
/// <param name="Options">兼容配置中的供应商选项；解释和校验只在 Provider 内进行。</param>
public sealed record ModelBinding(Guid ConfigId, int Version, string ProviderKey, string ModelId,
    Uri Endpoint, Guid? CredentialReference, IReadOnlyDictionary<string, string>? Options = null)
{
    /// <summary>诊断文本仅包含稳定标识，避免 record 自动输出凭据和供应商选项。</summary>
    public override string ToString() => $"ModelBinding {ConfigId} v{Version} ({ProviderKey})";
}
