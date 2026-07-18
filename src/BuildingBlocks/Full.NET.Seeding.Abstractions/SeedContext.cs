namespace Full.NET.Seeding.Abstractions;

/// <summary>
/// 提供单次 Seed Run 的非敏感运行元数据，不承载密码、令牌或连接信息。
/// </summary>
/// <param name="RunId">本次执行的全局唯一标识。</param>
/// <param name="Profile">调用方请求的目标 Profile。</param>
/// <param name="EnvironmentName">宿主环境名称。</param>
/// <param name="DefaultLocale">规范 BCP 47 默认语言标签。</param>
/// <param name="CorrelationId">部署或人工操作的关联标识。</param>
public sealed record SeedContext(
    Guid RunId,
    SeedProfile Profile,
    string EnvironmentName,
    string DefaultLocale,
    string CorrelationId);
