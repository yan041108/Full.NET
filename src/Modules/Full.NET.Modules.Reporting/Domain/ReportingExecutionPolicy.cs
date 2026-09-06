namespace Full.NET.Modules.Reporting.Domain;

/// <summary>报表有界执行策略常量；所有外部查询必须受此约束。</summary>
internal static class ReportingExecutionPolicy
{
    /// <summary>默认每页条数。</summary>
    public const int DefaultPageSize = 50;

    /// <summary>单页最大条数。</summary>
    public const int MaxPageSize = 200;

    /// <summary>外部数据库命令超时秒数。</summary>
    public const int CommandTimeoutSeconds = 30;

    /// <summary>连接打开超时秒数。</summary>
    public const int ConnectionTimeoutSeconds = 10;

    /// <summary>单个单元格返回值最大长度。</summary>
    public const int MaxCellValueLength = 4096;

    /// <summary>schema_inventory 参数 topN 允许的最大值。</summary>
    public const int MaxTopN = 200;
}
