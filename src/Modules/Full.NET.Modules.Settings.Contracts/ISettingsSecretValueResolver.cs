using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Settings.Contracts;

/// <summary>
/// Host 作用域 secret 配置项明文解析 Port；供 Jobs 等模块在 Worker 运行时解析密钥引用。
/// </summary>
public interface ISettingsSecretValueResolver
{
    /// <summary>
    /// 按 ConfigKey 解析已启用 secret 配置项的明文值；仅 Host 作用域可用。
    /// </summary>
    Task<Result<string>> ResolveSecretValueAsync(
        string configKey,
        CancellationToken cancellationToken = default);
}
