namespace Full.NET.Modules.Platform.Contracts;

/// <summary>
/// 授权备份任务支持的数据库提供程序范围。
/// </summary>
public static class BackupDatabaseProviders
{
    /// <summary>仅 SQL Server。</summary>
    public const string SqlServer = "sql_server";

    /// <summary>仅 MySQL。</summary>
    public const string MySql = "mysql";

    /// <summary>双库或跨提供程序任务。</summary>
    public const string All = "all";
}

/// <summary>
/// 授权备份运行状态机值。
/// </summary>
public static class BackupRunStatuses
{
    /// <summary>已登记，等待执行器领取。</summary>
    public const string Pending = "pending";

    /// <summary>执行器正在运行。</summary>
    public const string Running = "running";

    /// <summary>备份成功且产物可用。</summary>
    public const string Succeeded = "succeeded";

    /// <summary>备份失败。</summary>
    public const string Failed = "failed";
}

/// <remarks>
/// 机器码稳定性：字段顺序与权限码/错误码字符串发布后不可改名或删除，新增只能追加。
/// </remarks>
/// <summary>授权备份任务目录项。</summary>
/// <param name="Id">任务标识。</param>
/// <param name="TaskKey">稳定任务键，用于产物目录分段。</param>
/// <param name="DisplayName">展示名称。</param>
/// <param name="Description">任务说明。</param>
/// <param name="DatabaseProvider">目标数据库提供程序范围。</param>
/// <param name="IsEnabled">是否允许执行器领取。</param>
/// <param name="SortOrder">列表排序。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">更新时间（UTC）。</param>
public sealed record BackupTaskResponse(
    Guid Id,
    string TaskKey,
    string DisplayName,
    string? Description,
    string DatabaseProvider,
    bool IsEnabled,
    int SortOrder,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

/// <remarks>
/// 机器码稳定性：字段顺序与权限码/错误码字符串发布后不可改名或删除，新增只能追加。
/// </remarks>
/// <summary>授权备份运行结果。</summary>
/// <param name="Id">运行标识。</param>
/// <param name="TaskId">任务标识。</param>
/// <param name="TaskKey">任务键快照，便于列表展示。</param>
/// <param name="TaskDisplayName">任务展示名称快照。</param>
/// <param name="Status">运行状态。</param>
/// <param name="StartedAtUtc">开始时间（UTC）。</param>
/// <param name="CompletedAtUtc">完成时间（UTC）。</param>
/// <param name="ArtifactFileName">产物文件名；仅成功运行且配置产物根目录时可下载。</param>
/// <param name="ArtifactSizeBytes">产物大小（字节）。</param>
/// <param name="ArtifactContentType">产物内容类型。</param>
/// <param name="SummaryMessage">摘要或错误信息。</param>
/// <param name="CreatedAtUtc">记录创建时间（UTC）。</param>
/// <param name="CanDownload">当前运行是否满足受控下载前置条件。</param>
public sealed record BackupRunResponse(
    Guid Id,
    Guid TaskId,
    string TaskKey,
    string TaskDisplayName,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? ArtifactFileName,
    long? ArtifactSizeBytes,
    string? ArtifactContentType,
    string? SummaryMessage,
    DateTimeOffset CreatedAtUtc,
    bool CanDownload);

/// <summary>授权备份执行器部署与目录状态。</summary>
/// <param name="ArtifactRootPath">配置的相对产物根目录。</param>
/// <param name="ArtifactRootExists">产物根目录在 Api 进程内是否可访问。</param>
/// <param name="EnabledTaskCount">已启用任务数量。</param>
/// <param name="DeploymentNotice">部署期说明，强调凭据、对象存储与恢复演练边界。</param>
public sealed record BackupExecutorStatusResponse(
    string ArtifactRootPath,
    bool ArtifactRootExists,
    int EnabledTaskCount,
    string DeploymentNotice);
