namespace Full.NET.Modules.Reporting.Contracts;

/// <summary>Host 发布授权与租户执行目录的独立权限。</summary>
public static class ReportingDefinitionGrantPermissions
{
    /// <summary>读取 Host 管理的定义租户授权。</summary>
    public const string Read = "reporting.definition_grants.read";
    /// <summary>启用或撤销定义到租户的发布授权。</summary>
    public const string Manage = "reporting.definition_grants.manage";
    /// <summary>读取当前租户已获授权的发布目录，不授予定义编辑能力。</summary>
    public const string PublishedRead = "reporting.published_definitions.read";
}

/// <summary>设置显式发布授权；版本为零表示创建，更新须使用读取的当前版本。</summary>
/// <param name="IsEnabled">是否启用目标租户的执行授权。</param>
/// <param name="ExpectedVersion">预期版本，零仅允许创建新的启用授权。</param>
public sealed record SetReportingDefinitionTenantGrantRequest(bool IsEnabled, int ExpectedVersion);

/// <summary>Host 授权摘要；目标租户是明确管理对象，不是调用方可覆盖的当前上下文。</summary>
/// <param name="Id">授权标识。</param>
/// <param name="DefinitionId">报表定义标识。</param>
/// <param name="TenantId">被授权租户。</param>
/// <param name="IsEnabled">授权当前是否启用。</param>
/// <param name="Version">更新时必须提交的并发版本。</param>
/// <param name="CreatedAtUtc">创建 UTC 时间。</param>
/// <param name="UpdatedAtUtc">最近更新 UTC 时间。</param>
public sealed record ReportingDefinitionTenantGrantResponse(Guid Id, Guid DefinitionId, Guid TenantId,
    bool IsEnabled, int Version, DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc);

/// <summary>当前租户可执行的最小发布目录，不包含数据源、连接、布局或 SQL。</summary>
/// <param name="Id">定义标识。</param>
/// <param name="DefinitionKey">稳定定义键。</param>
/// <param name="Name">显示名称。</param>
/// <param name="VersionNumber">当前发布版本，创建导出时固定此值。</param>
/// <param name="ParameterSchema">公开的执行参数 Schema。</param>
public sealed record ReportingPublishedDefinitionResponse(Guid Id, string DefinitionKey, string Name,
    int VersionNumber, IReadOnlyList<ReportingParameterSchemaEntry> ParameterSchema);
