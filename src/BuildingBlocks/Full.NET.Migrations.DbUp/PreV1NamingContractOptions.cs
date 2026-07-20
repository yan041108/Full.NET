namespace Full.NET.Migrations.DbUp;

/// <summary>
/// 提供 011 Pre-v1 Naming Contract 迁移所需的维护窗口证据。
/// </summary>
/// <remarks>
/// 所有门禁默认关闭；该配置只供 Migrator 使用，API 与 Worker 不得继承这些豁免。
/// </remarks>
public sealed class PreV1NamingContractOptions
{
    /// <summary>
    /// 获取配置节名称。
    /// </summary>
    public const string SectionName = "PreV1NamingContract";

    /// <summary>
    /// 获取或设置是否已进入停止业务流量的维护窗口。
    /// </summary>
    public bool MaintenanceMode { get; set; }

    /// <summary>
    /// 获取或设置是否已验证维护窗口前备份可恢复。
    /// </summary>
    public bool BackupVerified { get; set; }

    /// <summary>
    /// 获取或设置旧应用、Worker 与所有直写工具是否已停止。
    /// </summary>
    public bool LegacyWritersStopped { get; set; }

    /// <summary>
    /// 获取或设置 legacy Outbox 待处理消息是否已排空或已回填 canonical 列。
    /// </summary>
    public bool LegacyOutboxDrained { get; set; }

    /// <summary>
    /// 获取或设置已批准的破坏性 DDL 豁免标识。
    /// </summary>
    public string DestructiveDdlApprovalId { get; set; } = string.Empty;
}
