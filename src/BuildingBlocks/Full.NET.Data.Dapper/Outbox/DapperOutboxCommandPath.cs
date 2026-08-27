namespace Full.NET.Data.Dapper.Outbox;

/// <summary>
/// Outbox 写入的内部命令路径；生产默认保持静态 Registry，Typed Plan 只用于显式 A/B。
/// </summary>
internal enum DapperOutboxCommandPath
{
    StaticRegistry,
    TypedPlan,
}
