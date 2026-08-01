namespace Full.NET.Abstractions.Auditing;

/// <summary>
/// 审计可靠性等级：决定审计写入与触发它的业务操作之间的耦合方式与投递保证。
/// </summary>
public enum AuditReliabilityClass
{
    /// <summary>
    /// B0：域内同事务审计。写入必须与触发它的业务写入共享同一数据库事务，
    /// 同提交、同回滚，禁止使用 Outbox 或任何异步补偿路径。
    /// </summary>
    DomainTransactional,

    /// <summary>
    /// B1：重要 HTTP 操作审计。请求等待有界微批写入尝试，默认 fail-open + 告警；
    /// 禁止使用 Outbox，要求“无审计不成功”的动作必须改归 B0。
    /// </summary>
    ImportantHttp,

    /// <summary>
    /// B2：普通 HTTP Operation Log / Access / 诊断遥测。进入有界结构化日志管道，
    /// 可采样或丢弃，不写业务主库，也不使用 Outbox。
    /// </summary>
    BestEffort,
}
