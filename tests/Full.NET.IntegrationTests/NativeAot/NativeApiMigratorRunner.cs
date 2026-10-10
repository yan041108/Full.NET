using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Migrations.DbUp;
using Full.NET.Testing;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>
/// 通过 JIT Migrator 子进程准备数据库 schema，满足 Native E2E 的数据库前置条件。
/// </summary>
internal static class NativeApiMigratorRunner
{
    public static async Task MigrateAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var repositoryRoot = NativeApiArtifactLocator.FindRepositoryRoot();
        var startInfo = NativeMigratorProcess.CreateStartInfo(repositoryRoot, AppContext.BaseDirectory);

        startInfo.Environment["DOTNET_ENVIRONMENT"] = "Testing";
        startInfo.Environment[$"{DatabaseOptions.SectionName}__Provider"] =
            provider.ToString();
        startInfo.Environment[$"{DatabaseOptions.SectionName}__ConnectionString"] =
            connectionString;
        startInfo.Environment[$"{DatabaseOptions.SectionName}__CommandTimeoutSeconds"] =
            "300";
        startInfo.Environment[$"{DatabaseOptions.SectionName}__MySqlGuidStorageMode"] =
            "Binary16";
        startInfo.Environment["Identity__Bootstrap__Username"] = "admin";
        startInfo.Environment["Identity__Bootstrap__Password"] =
            NativeApiE2EAssertions.AdminPassword;
        startInfo.Environment["Identity__Bootstrap__DisplayName"] = "系统管理员";
        startInfo.Environment["Identity__AllowDevelopmentEphemeralSigningKey"] = "true";
        ApplyMigrationContractGates(startInfo.Environment);

        await NativeMigratorProcess.RunAsync(startInfo, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 009/011 破坏性 DDL 迁移要求维护窗口证据；与集成测试 <see cref="MigrationContractOptionFactory"/> 对齐。
    /// </summary>
    private static void ApplyMigrationContractGates(
        IDictionary<string, string?> environment)
    {
        environment[$"{UuidBinaryContractOptions.SectionName}__MaintenanceMode"] = "true";
        environment[$"{UuidBinaryContractOptions.SectionName}__BackupVerified"] = "true";
        environment[$"{UuidBinaryContractOptions.SectionName}__LegacyWritersStopped"] = "true";
        environment[$"{UuidBinaryContractOptions.SectionName}__DestructiveDdlApprovalId"] =
            MigrationContractOptionFactory.UuidApprovalId;
        environment[$"{PreV1NamingContractOptions.SectionName}__MaintenanceMode"] = "true";
        environment[$"{PreV1NamingContractOptions.SectionName}__BackupVerified"] = "true";
        environment[$"{PreV1NamingContractOptions.SectionName}__LegacyWritersStopped"] = "true";
        environment[$"{PreV1NamingContractOptions.SectionName}__LegacyOutboxDrained"] = "true";
        environment[$"{PreV1NamingContractOptions.SectionName}__DestructiveDdlApprovalId"] =
            MigrationContractOptionFactory.NamingApprovalId;
    }
}
