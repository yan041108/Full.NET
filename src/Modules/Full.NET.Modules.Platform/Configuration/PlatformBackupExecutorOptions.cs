namespace Full.NET.Modules.Platform.Configuration;

/// <summary>
/// 授权备份执行器产物根目录配置；凭据与真实备份执行不在 Api 进程内完成。
/// </summary>
public sealed class PlatformBackupExecutorOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "FullNet:Platform:BackupExecutor";

    /// <summary>
    /// 备份产物相对根目录，按 <c>{TaskKey}/{ArtifactFileName}</c> 组织；
    /// 绝对路径按 Api ContentRoot 解析。
    /// </summary>
    public string ArtifactRootPath { get; init; } = "App_Data/backup-artifacts";

    /// <summary>部署期说明，返回给管理端提示凭据、对象存储与恢复演练边界。</summary>
    public string DeploymentNotice { get; init; } =
        "授权备份执行器仅在部署期配置凭据与对象存储后由独立执行器写入产物；本页提供任务目录、运行结果与受控下载，不包含生产恢复操作。";
}
