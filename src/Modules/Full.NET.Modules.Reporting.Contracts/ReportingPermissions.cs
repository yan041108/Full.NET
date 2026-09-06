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

/// <summary>Reporting 分组管理权限码。</summary>
public static class ReportingGroupPermissions
{
    /// <summary>读取报表分组。</summary>
    public const string Read = "reporting.groups.read";

    /// <summary>创建报表分组。</summary>
    public const string Create = "reporting.groups.create";

    /// <summary>更新报表分组。</summary>
    public const string Update = "reporting.groups.update";

    /// <summary>删除报表分组。</summary>
    public const string Delete = "reporting.groups.delete";
}

/// <summary>Reporting 定义管理权限码。</summary>
public static class ReportingDefinitionPermissions
{
    /// <summary>读取报表定义与版本。</summary>
    public const string Read = "reporting.definitions.read";

    /// <summary>创建报表定义。</summary>
    public const string Create = "reporting.definitions.create";

    /// <summary>更新报表定义草稿。</summary>
    public const string Update = "reporting.definitions.update";

    /// <summary>删除报表定义。</summary>
    public const string Delete = "reporting.definitions.delete";

    /// <summary>发布报表定义版本。</summary>
    public const string Publish = "reporting.definitions.publish";
}

/// <summary>Reporting 静态 Query Port 目录权限码。</summary>
public static class ReportingQueryPortPermissions
{
    /// <summary>读取静态 Query Port 目录。</summary>
    public const string Read = "reporting.query_ports.read";
}
