using Full.NET.Migrations.DbUp;
using Full.NET.Seeding.Abstractions;
using Full.NET.Seeding.Dapper;

namespace Full.NET.Host.Migrator;

/// <summary>
/// 描述迁移成功后可选 Seed 阶段的非敏感执行摘要。
/// </summary>
/// <param name="ExecutedScriptCount">本次执行的数据库迁移脚本数量。</param>
/// <param name="SeedProfile">实际请求的 Seed Profile；为空表示只迁移。</param>
/// <param name="UsesLegacyAlias">是否通过待退役的 <c>--seed-local</c> 请求 Development。</param>
internal sealed record MigratorWorkflowResult(
    int ExecutedScriptCount,
    SeedProfile? SeedProfile,
    bool UsesLegacyAlias);

/// <summary>
/// 集中声明 Migrator 向命令行公开的稳定错误码。
/// </summary>
internal static class MigratorErrorCodes
{
    public const string MigrationFailed = "migrator.migration.failed";

    public const string ExecutionCancelled = "migrator.execution.cancelled";

    public const string ExecutionFailed = "migrator.execution.failed";
}

/// <summary>
/// 表示 Migrator 已归类的安全失败；消息只包含稳定错误码，完整原因仅作为内部异常保留。
/// </summary>
internal sealed class MigratorWorkflowException : Exception
{
    public MigratorWorkflowException(string code, Exception? innerException = null)
        : base(code, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
    }

    public string Code { get; }
}

/// <summary>
/// 按“迁移成功后才允许写入 Seed”的顺序执行 Migrator 工作流。
/// </summary>
internal sealed class MigratorWorkflow(
    IDatabaseMigrationRunner migrationRunner,
    ISeedOrchestrator seedOrchestrator)
{
    /// <summary>
    /// 先执行数据库迁移，再按显式 CLI 选择运行 Seed；迁移失败会阻断全部后续写入。
    /// </summary>
    public async Task<MigratorWorkflowResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        SeedCommandLineOptions commandLine;
        try
        {
            commandLine = SeedCommandLine.Parse(arguments);
        }
        catch (SeedConfigurationException exception)
        {
            throw new MigratorWorkflowException(exception.Code, exception);
        }

        MigrationResult migration;
        try
        {
            migration = await migrationRunner
                .MigrateAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new MigratorWorkflowException(
                MigratorErrorCodes.MigrationFailed,
                exception);
        }

        if (!migration.Successful)
        {
            throw new MigratorWorkflowException(MigratorErrorCodes.MigrationFailed);
        }

        if (commandLine.Profile.HasValue)
        {
            var seed = await seedOrchestrator
                .RunAsync(commandLine.Profile.Value, cancellationToken)
                .ConfigureAwait(false);
            if (!seed.IsSuccess)
            {
                throw new MigratorWorkflowException(
                    seed.Error?.Code ?? SeedErrorCodes.ContributorFailed);
            }
        }

        return new MigratorWorkflowResult(
            migration.ExecutedScriptCount,
            commandLine.Profile,
            commandLine.UsesLegacyAlias);
    }
}
