namespace Full.NET.Modules.K3Cloud.Contracts;

/// <summary>K3Cloud 连接配置权限码。</summary>
public static class K3CloudConnectionPermissions
{
    /// <summary>读取 K3Cloud 连接配置。</summary>
    public const string Read = "k3cloud.connections.read";

    /// <summary>创建 K3Cloud 连接配置。</summary>
    public const string Create = "k3cloud.connections.create";

    /// <summary>更新 K3Cloud 连接配置。</summary>
    public const string Update = "k3cloud.connections.update";

    /// <summary>测试 K3Cloud 连接（ValidateUser）。</summary>
    public const string Test = "k3cloud.connections.test";
}

/// <summary>K3Cloud 单据同步权限码。</summary>
public static class K3CloudDocumentSyncPermissions
{
    /// <summary>读取 K3Cloud 单据同步记录。</summary>
    public const string Read = "k3cloud.document_syncs.read";

    /// <summary>创建 K3Cloud 单据同步任务。</summary>
    public const string Create = "k3cloud.document_syncs.create";

    /// <summary>重试失败的 K3Cloud 单据同步。</summary>
    public const string Retry = "k3cloud.document_syncs.retry";
}
