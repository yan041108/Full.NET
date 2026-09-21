namespace Full.NET.Modules.Platform.Contracts;

/// <summary>
/// 平台更新日志相关操作的稳定权限码，不可本地化且作为服务端授权与客户端可见性的共同权威。
/// </summary>
public static class PlatformPermissions
{
    /// <summary>允许查询更新日志（Host 管理端与终端用户已发布列表）。</summary>
    public const string Read = "platform.release_notes.read";

    /// <summary>允许进入 Host 更新日志管理页并查询管理列表/详情。</summary>
    public const string HostManageRead = "platform.host_release_notes.read";

    /// <summary>允许创建更新日志草稿。</summary>
    public const string Create = "platform.release_notes.create";

    /// <summary>允许更新更新日志草稿。</summary>
    public const string Update = "platform.release_notes.update";

    /// <summary>允许发布更新日志草稿。</summary>
    public const string Publish = "platform.release_notes.publish";

    /// <summary>允许撤回已发布更新日志。</summary>
    public const string Retract = "platform.release_notes.retract";

    /// <summary>允许删除更新日志草稿。</summary>
    public const string Delete = "platform.release_notes.delete";

    /// <summary>允许将已发布更新日志标记为已读。</summary>
    public const string MarkRead = "platform.release_notes.mark_read";

    /// <summary>允许查询授权备份任务目录。</summary>
    public const string BackupTasksRead = "platform.backup_tasks.read";

    /// <summary>允许查询授权备份运行结果。</summary>
    public const string BackupRunsRead = "platform.backup_runs.read";

    /// <summary>允许受控下载已成功备份产物。</summary>
    public const string BackupRunsDownload = "platform.backup_runs.download";
}
