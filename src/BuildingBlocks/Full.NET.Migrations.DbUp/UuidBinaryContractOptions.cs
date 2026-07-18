using System.Text.RegularExpressions;

namespace Full.NET.Migrations.DbUp;

/// <summary>
/// 提供 009 UUID Contract 迁移所需的维护窗口证据。
/// </summary>
/// <remarks>
/// 所有门禁默认关闭；该配置只供 Migrator 使用，API 与 Worker 不得继承这些豁免。
/// </remarks>
public sealed partial class UuidBinaryContractOptions
{
    /// <summary>
    /// 获取配置节名称。
    /// </summary>
    public const string SectionName = "UuidBinaryContract";

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
    /// 获取或设置已批准的破坏性 DDL 豁免标识。
    /// </summary>
    public string DestructiveDdlApprovalId { get; set; } = string.Empty;

    internal static bool IsApprovalIdValid(string value) =>
        ApprovalIdPattern().IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ApprovalIdPattern();
}
