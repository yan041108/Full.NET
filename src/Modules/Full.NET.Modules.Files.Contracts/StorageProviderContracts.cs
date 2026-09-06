namespace Full.NET.Modules.Files.Contracts;

/// <summary>存储 Provider 管理只读与连通性测试权限。</summary>
public static class StorageProviderPermissions
{
    /// <summary>查看已注册存储 Provider 目录与配置摘要。</summary>
    public const string Read = "files.storage_providers.read";

    /// <summary>对支持远程探测的 Provider 执行受界连通性测试。</summary>
    public const string Test = "files.storage_providers.test";
}

/// <summary>已注册存储 Provider 目录项。</summary>
/// <param name="ProviderKey">稳定机器码。</param>
/// <param name="DisplayName">管理端展示名称。</param>
/// <param name="Kind">实现类别：local、s3 或 oss。</param>
/// <param name="IsDefault">是否为当前默认上传 Provider。</param>
/// <param name="IsConfigured">宿主配置是否满足最小运行条件。</param>
/// <param name="ConfigurationSummary">脱敏后的配置摘要；不得包含密钥。</param>
/// <param name="SupportsConnectivityTest">是否支持远程连通性测试。</param>
public sealed record StorageProviderCatalogItem(
    string ProviderKey,
    string DisplayName,
    string Kind,
    bool IsDefault,
    bool IsConfigured,
    string? ConfigurationSummary,
    bool SupportsConnectivityTest);

/// <summary>存储 Provider 连通性测试结果。</summary>
/// <param name="Succeeded">是否判定为连通成功。</param>
/// <param name="Message">面向管理员的诊断说明；不得包含密钥。</param>
public sealed record TestStorageProviderConnectivityResult(
    bool Succeeded,
    string Message);
