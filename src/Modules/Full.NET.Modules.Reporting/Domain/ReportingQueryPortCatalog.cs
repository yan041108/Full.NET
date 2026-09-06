using Full.NET.Modules.Reporting.Contracts;

namespace Full.NET.Modules.Reporting.Domain;

/// <summary>内置静态 Query Port 目录；所有 SQL 仅存在于服务端审查过的实现中。</summary>
internal static class ReportingQueryPortCatalog
{
    private static readonly IReadOnlyList<ReportingQueryPortDefinition> Ports =
    [
        new(
            "reporting.database_engine_version",
            "数据库引擎版本",
            "返回目标数据库引擎版本信息，用于验证只读连接与后续报表执行前置检查。",
            [ReportingDataSourceProviderKeys.SqlServer, ReportingDataSourceProviderKeys.MySql],
            []),
        new(
            "reporting.schema_inventory",
            "Schema 清单",
            "列出当前数据库可见 Schema/库名，不包含用户自定义 SQL。",
            [ReportingDataSourceProviderKeys.SqlServer, ReportingDataSourceProviderKeys.MySql],
            [
                new ReportingQueryPortParameterDefinition(
                    "topN",
                    "返回条数上限",
                    ReportingParameterDataTypeKeys.Integer,
                    true,
                    "20",
                    1,
                    200),
            ]),
    ];

    /// <summary>返回全部静态 Query Port 定义。</summary>
    public static IReadOnlyList<ReportingQueryPortDefinition> List() => Ports;

    /// <summary>按键解析 Query Port；不存在时返回 <see langword="null"/>。</summary>
    public static ReportingQueryPortDefinition? TryGet(string queryPortKey) =>
        Ports.FirstOrDefault(port =>
            string.Equals(port.QueryPortKey, queryPortKey, StringComparison.Ordinal));

    /// <summary>返回 Query Port 在指定提供程序下可执行的审查 SQL。</summary>
    /// <param name="queryPortKey">Query Port 键。</param>
    /// <param name="providerKey">数据源提供程序键。</param>
    /// <returns>只读 SQL；不支持组合时返回 <see langword="null"/>。</returns>
    public static string? ResolveSql(string queryPortKey, string providerKey) =>
        (queryPortKey, providerKey) switch
        {
            ("reporting.database_engine_version", ReportingDataSourceProviderKeys.SqlServer) =>
                "SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)) AS EngineVersion",
            ("reporting.database_engine_version", ReportingDataSourceProviderKeys.MySql) =>
                "SELECT VERSION() AS EngineVersion",
            ("reporting.schema_inventory", ReportingDataSourceProviderKeys.SqlServer) =>
                "SELECT TOP (@TopN) name AS SchemaName FROM sys.schemas ORDER BY name",
            ("reporting.schema_inventory", ReportingDataSourceProviderKeys.MySql) =>
                "SELECT schema_name AS SchemaName FROM information_schema.schemata ORDER BY schema_name LIMIT @TopN",
            _ => null,
        };
}
