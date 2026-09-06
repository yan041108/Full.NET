namespace Full.NET.Modules.K3Cloud.Contracts;

/// <summary>K3Cloud 模块稳定业务错误码。</summary>
public static class K3CloudErrorCodes
{
    /// <summary>连接配置不存在。</summary>
    public const string ConnectionNotFound = "k3cloud.connection.not_found";

    /// <summary>连接配置元数据校验失败。</summary>
    public const string ConnectionInvalid = "k3cloud.connection.invalid";

    /// <summary>连接配置并发版本冲突。</summary>
    public const string ConnectionConcurrencyConflict = "k3cloud.connection.concurrency_conflict";

    /// <summary>连接测试失败。</summary>
    public const string ConnectionTestFailed = "k3cloud.connection.test_failed";

    /// <summary>单据类型不受支持。</summary>
    public const string DocumentTypeUnsupported = "k3cloud.document_type.unsupported";

    /// <summary>单据同步记录不存在。</summary>
    public const string DocumentSyncNotFound = "k3cloud.document_sync.not_found";

    /// <summary>单据同步业务键冲突。</summary>
    public const string DocumentSyncBusinessKeyConflict = "k3cloud.document_sync.business_key_conflict";

    /// <summary>单据同步载荷无效。</summary>
    public const string DocumentSyncPayloadInvalid = "k3cloud.document_sync.payload_invalid";

    /// <summary>单据同步状态不允许重试。</summary>
    public const string DocumentSyncRetryNotAllowed = "k3cloud.document_sync.retry_not_allowed";

    /// <summary>K3Cloud 远程调用失败。</summary>
    public const string RemoteCallFailed = "k3cloud.remote.call_failed";
}
