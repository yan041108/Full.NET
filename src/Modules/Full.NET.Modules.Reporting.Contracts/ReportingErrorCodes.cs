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
}
