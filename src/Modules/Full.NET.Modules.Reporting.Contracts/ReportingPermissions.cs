namespace Full.NET.Modules.Reporting.Contracts;

/// <summary>Reporting 数据源管理权限码。</summary>
public static class ReportingDataSourcePermissions
{
    /// <summary>分页查询报表数据源。</summary>
    public const string Read = "reporting.data_sources.read";

    /// <summary>创建报表数据源。</summary>
    public const string Create = "reporting.data_sources.create";

    /// <summary>更新报表数据源。</summary>
    public const string Update = "reporting.data_sources.update";

    /// <summary>删除报表数据源。</summary>
    public const string Delete = "reporting.data_sources.delete";

    /// <summary>测试报表数据源连接。</summary>
    public const string Test = "reporting.data_sources.test";
}
