namespace Full.NET.Modules.Reporting.Contracts;

/// <summary>Reporting 模块稳定业务错误码。</summary>
public static class ReportingErrorCodes
{
    /// <summary>报表数据源不存在。</summary>
    public const string DataSourceNotFound = "reporting.data_source.not_found";

    /// <summary>报表数据源元数据校验失败。</summary>
    public const string DataSourceInvalid = "reporting.data_source.invalid";

    /// <summary>创建报表数据源时密码必填。</summary>
    public const string DataSourcePasswordRequired = "reporting.data_source.password_required";

    /// <summary>报表数据源提供程序不受支持。</summary>
    public const string DataSourceProviderUnsupported = "reporting.data_source.provider_unsupported";

    /// <summary>报表数据源连接测试失败。</summary>
    public const string DataSourceTestFailed = "reporting.data_source.test_failed";

    /// <summary>报表数据源并发版本冲突。</summary>
    public const string DataSourceConcurrencyConflict = "reporting.data_source.concurrency_conflict";

    /// <summary>报表分组不存在。</summary>
    public const string GroupNotFound = "reporting.group.not_found";

    /// <summary>报表分组元数据校验失败。</summary>
    public const string GroupInvalid = "reporting.group.invalid";

    /// <summary>报表分组并发版本冲突。</summary>
    public const string GroupConcurrencyConflict = "reporting.group.concurrency_conflict";

    /// <summary>报表分组存在子分组或定义引用，无法删除。</summary>
    public const string GroupInUse = "reporting.group.in_use";

    /// <summary>报表定义不存在。</summary>
    public const string DefinitionNotFound = "reporting.definition.not_found";

    /// <summary>报表定义元数据校验失败。</summary>
    public const string DefinitionInvalid = "reporting.definition.invalid";

    /// <summary>报表定义键已存在。</summary>
    public const string DefinitionKeyConflict = "reporting.definition.key_conflict";

    /// <summary>报表定义并发版本冲突。</summary>
    public const string DefinitionConcurrencyConflict = "reporting.definition.concurrency_conflict";

    /// <summary>静态 Query Port 不存在。</summary>
    public const string QueryPortNotFound = "reporting.query_port.not_found";

    /// <summary>参数 Schema 与 Query Port 不匹配。</summary>
    public const string ParameterSchemaInvalid = "reporting.parameter_schema.invalid";

    /// <summary>报表定义版本不存在。</summary>
    public const string DefinitionVersionNotFound = "reporting.definition_version.not_found";
}
