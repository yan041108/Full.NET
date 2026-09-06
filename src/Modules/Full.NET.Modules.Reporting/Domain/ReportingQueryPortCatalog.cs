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

    /// <summary>返回 Query Port 在指定提供程序下可执行的审查 SQL（含 topN 等参数占位符）。</summary>
    public static string? ResolveSql(string queryPortKey, string providerKey) =>
        ResolveBaseSql(queryPortKey, providerKey) is null
            ? null
            : queryPortKey switch
            {
                "reporting.schema_inventory" when providerKey == ReportingDataSourceProviderKeys.SqlServer =>
                    "SELECT TOP (@TopN) name AS SchemaName FROM sys.schemas ORDER BY name",
                "reporting.schema_inventory" when providerKey == ReportingDataSourceProviderKeys.MySql =>
                    "SELECT schema_name AS SchemaName FROM information_schema.schemata ORDER BY schema_name LIMIT @TopN",
                _ => ResolveBaseSql(queryPortKey, providerKey),
            };

    /// <summary>返回不带分页包装的基础只读 SQL。</summary>
    public static string? ResolveBaseSql(string queryPortKey, string providerKey) =>
        (queryPortKey, providerKey) switch
        {
            ("reporting.database_engine_version", ReportingDataSourceProviderKeys.SqlServer) =>
                "SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)) AS EngineVersion",
            ("reporting.database_engine_version", ReportingDataSourceProviderKeys.MySql) =>
                "SELECT VERSION() AS EngineVersion",
            ("reporting.schema_inventory", ReportingDataSourceProviderKeys.SqlServer) =>
                "SELECT name AS SchemaName FROM sys.schemas ORDER BY name",
            ("reporting.schema_inventory", ReportingDataSourceProviderKeys.MySql) =>
                "SELECT schema_name AS SchemaName FROM information_schema.schemata ORDER BY schema_name",
            _ => null,
        };

    /// <summary>返回用于分页排序的稳定列名。</summary>
    public static string? ResolveOrderByColumn(string queryPortKey) =>
        queryPortKey switch
        {
            "reporting.schema_inventory" => "SchemaName",
            _ => null,
        };

    /// <summary>返回 Query Port 是否支持服务端分页。</summary>
    public static bool SupportsPagination(string queryPortKey) =>
        string.Equals(queryPortKey, "reporting.schema_inventory", StringComparison.Ordinal);

    /// <summary>返回 Query Port 的默认结果列键。</summary>
    public static IReadOnlyList<string> ResolveResultColumnKeys(string queryPortKey) =>
        queryPortKey switch
        {
            "reporting.database_engine_version" => ["EngineVersion"],
            "reporting.schema_inventory" => ["SchemaName"],
            _ => [],
        };
}
