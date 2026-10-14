namespace Full.NET.AI.Abstractions.Models;
/// <summary>由业务授权后签发的模型绑定；只携带短期不透明凭据引用，不含明文或密文。</summary>
/// <param name="ConfigId">配置标识。</param><param name="Version">配置版本。</param>
/// <param name="ProviderKey">静态提供程序键。</param><param name="ModelId">模型标识。</param>
/// <param name="Endpoint">已配置的供应商地址。</param><param name="CredentialReference">请求作用域内的短期引用，不可持久化或跨作用域使用。</param>
/// <param name="Options">兼容配置中的供应商选项；解释和校验只在 Provider 内进行。</param>
/// <param name="DataClassification">由业务权威来源冻结的数据分类；未知分类拒绝，无来源证明的输入按 restricted 处理。</param>
public sealed record ModelBinding(Guid ConfigId, int Version, string ProviderKey, string ModelId,
    Uri Endpoint, Guid? CredentialReference, IReadOnlyDictionary<string, string>? Options, string DataClassification)
{
    /// <summary>保留原七字段构造签名；没有分类证明的旧调用统一按 restricted 处理。</summary>
    /// <param name="ConfigId">配置标识。</param><param name="Version">配置版本。</param>
    /// <param name="ProviderKey">静态提供程序键。</param><param name="ModelId">模型标识。</param>
    /// <param name="Endpoint">配置地址。</param><param name="CredentialReference">作用域内凭据引用。</param>
    /// <param name="Options">供应商选项。</param>
    public ModelBinding(Guid ConfigId, int Version, string ProviderKey, string ModelId, Uri Endpoint,
        Guid? CredentialReference, IReadOnlyDictionary<string, string>? Options = null)
        : this(ConfigId, Version, ProviderKey, ModelId, Endpoint, CredentialReference, Options, "restricted") { }

    /// <summary>保留旧解构 ABI；分类只由新契约明确读取，不会静默成为低敏数据。</summary>
    /// <param name="ConfigId">配置标识。</param><param name="Version">版本。</param>
    /// <param name="ProviderKey">供应商键。</param><param name="ModelId">模型标识。</param>
    /// <param name="Endpoint">配置地址。</param><param name="CredentialReference">短期引用。</param>
    /// <param name="Options">供应商选项。</param>
    public void Deconstruct(out Guid ConfigId, out int Version, out string ProviderKey, out string ModelId,
        out Uri Endpoint, out Guid? CredentialReference, out IReadOnlyDictionary<string, string>? Options)
    {
        ConfigId = this.ConfigId; Version = this.Version; ProviderKey = this.ProviderKey; ModelId = this.ModelId;
        Endpoint = this.Endpoint; CredentialReference = this.CredentialReference; Options = this.Options;
    }
    /// <summary>诊断文本仅包含稳定标识，避免 record 自动输出凭据和供应商选项。</summary>
    public override string ToString() => $"ModelBinding {ConfigId} v{Version} ({ProviderKey})";
}
