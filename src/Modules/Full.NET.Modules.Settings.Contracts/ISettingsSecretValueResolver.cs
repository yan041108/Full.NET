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
    /// <param name="configKey">已启用 secret 配置项的稳定键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功时为 secret 的明文值；配置项不存在、已禁用或非 Host 作用域时通过 Result.Error 返回错误，调用方须先判断 IsSuccess。</returns>
    Task<Result<string>> ResolveSecretValueAsync(
        string configKey,
        CancellationToken cancellationToken = default);
}
