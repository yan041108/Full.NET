using System.Data.Common;
using System.Security.Cryptography;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Migrations;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>原生一次性宿主执行真实迁移、播种和失败门禁；不借用 JIT Migrator 准备数据库。</summary>
[TestClass, DoNotParallelize]
public sealed class NativeMigratorE2ETests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Native_migrator_runs_baseline_idempotently_and_rejects_production_overlay(DatabaseProvider provider)
    {
        var executable = NativeIndependentHostProcess.RequireArtifact("migrator");
        var connectionString = await CreateDatabaseAsync(provider);
        await using var db = Connection(provider, connectionString);
        await db.OpenAsync();
        var environment = EnvironmentFor(provider, connectionString);
        await SuccessfulAsync("baseline");
        var journal = provider == DatabaseProvider.SqlServer ? "dbo.SchemaVersions" : "schemaversions";
        var first = await SnapshotAsync();
        Assert.IsTrue(first[0] > 0);
        Assert.AreEqual(1, first[1], "Baseline 必须实际引导唯一管理员。");
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_tenancy_tenant WHERE Identifier='local'"));
        Assert.AreEqual(first[0], await db.QuerySingleAsync<int>($"SELECT COUNT(DISTINCT ScriptName) FROM {journal}"));
        var expected = Environment.GetEnvironmentVariable("FULLNET_NATIVE_MIGRATOR_EXPECTED_SCRIPTS_" + provider.ToString().ToUpperInvariant());
        Assert.IsTrue(int.TryParse(expected, out var scriptCount) && scriptCount > 0, "必须提供本次冻结发布包的脚本数量。");
        Assert.AreEqual(scriptCount, first[0], "journal 必须覆盖同次发布包的全部提供程序脚本。");
        await SuccessfulAsync("baseline");
        CollectionAssert.AreEqual(first, await SnapshotAsync(), "同包重跑不得重复业务数据或迁移记账。");
        Assert.AreEqual(2, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_seed_run WHERE CompletedAt IS NOT NULL AND ErrorCode IS NULL"));
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_seed_run_item WHERE CompletedAt IS NULL OR ErrorCode IS NOT NULL"));

        environment["DOTNET_ENVIRONMENT"] = "Production";
        var beforeRuns = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_seed_run");
        var beforeItems = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_seed_run_item");
        await using (var rejected = NativeIndependentHostProcess.Start(executable, ["migrate", "--seed", "development"], environment, "migrator-overlay"))
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var result = await rejected.WaitAsync(limit.Token);
            Assert.AreEqual(1, result.ExitCode, result.Errors + result.Output);
            StringAssert.Contains(result.Errors, "seeding.profile.not_allowed");
        }
        Assert.AreEqual(beforeRuns, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_seed_run"));
        Assert.AreEqual(beforeItems, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_seed_run_item"));
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_tenancy_tenant WHERE Identifier='local'"));
        environment["DOTNET_ENVIRONMENT"] = "Testing";
        await SuccessfulAsync("development");
        Assert.AreEqual(1, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_tenancy_tenant WHERE Identifier='local'"));
        Assert.AreEqual(1, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_identity_tenant_member WHERE Status='Active'"));
        await SuccessfulAsync("development");
        Assert.AreEqual(1, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_tenancy_tenant WHERE Identifier='local'"));
        Assert.AreEqual(1, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_identity_tenant_member WHERE Status='Active'"));

        // 显式开启可选 OIDC 播种，验证实际静态 Store、密钥登记及重跑不覆盖管理员策略。
        using var signingKey = RSA.Create(2048);
        environment["Identity__Oidc__Enable"] = "true";
        environment["Identity__Oidc__Issuer"] = "https://native-migrator.invalid";
        environment["Identity__Oidc__AllowDevelopmentEphemeralSigningKey"] = "false";
        environment["Identity__Oidc__ActiveSigningKeyId"] = "native-seed";
        environment["Identity__Oidc__SigningKeys__native-seed__PublicKeyPem"] = signingKey.ExportRSAPublicKeyPem();
        environment["Identity__Oidc__SigningKeys__native-seed__PrivateKeyPem"] = signingKey.ExportRSAPrivateKeyPem();
        environment["Identity__Oidc__EncryptionKeyBase64"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        environment["Identity__Oidc__Clients__0__ClientId"] = "native-seed-client";
        environment["Identity__Oidc__Clients__0__ClientSecret"] = "Native-Seed-TestOnly-Secret-!2026";
        environment["Identity__Oidc__Clients__0__RedirectUris__0"] = "https://native-migrator.invalid/callback";
        environment["Identity__Oidc__Clients__0__Scopes__0"] = "native-seed-scope";
        await SuccessfulAsync("baseline");
        Assert.AreEqual(1, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_identity_oidc_application WHERE ClientId='native-seed-client'"));
        Assert.AreEqual(1, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_identity_oidc_scope WHERE Name='native-seed-scope'"));
        Assert.AreEqual(1, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_identity_oidc_signing_key WHERE KeyId='native-seed' AND IsRetired=0"));
        Assert.AreEqual(Convert.ToHexString(SHA256.HashData(signingKey.ExportSubjectPublicKeyInfo())),
            await db.QuerySingleAsync<string>("SELECT PublicKeyHash FROM fn_identity_oidc_signing_key WHERE KeyId='native-seed'"));
        Assert.AreEqual("native-seed", await db.QuerySingleAsync<string>("SELECT ActiveKeyId FROM fn_identity_oidc_signing_policy WHERE PolicyKey='default'"));
        var retainedSecret = await db.QuerySingleAsync<string>("SELECT ClientSecret FROM fn_identity_oidc_application WHERE ClientId='native-seed-client'");
        Assert.IsFalse(string.IsNullOrWhiteSpace(retainedSecret), "机密客户端必须实际保存非空密钥哈希。");
        Assert.AreNotEqual(environment["Identity__Oidc__Clients__0__ClientSecret"], retainedSecret);
        await db.ExecuteAsync("UPDATE fn_identity_oidc_application SET DisplayName='native-retained-policy' WHERE ClientId='native-seed-client'");
        await SuccessfulAsync("baseline");
        Assert.AreEqual(1, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_identity_oidc_application WHERE ClientId='native-seed-client'"));
        Assert.AreEqual(1, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_identity_oidc_scope WHERE Name='native-seed-scope'"));
        Assert.AreEqual(1, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_identity_oidc_signing_key WHERE KeyId='native-seed'"));
        Assert.AreEqual("native-retained-policy", await db.QuerySingleAsync<string>("SELECT DisplayName FROM fn_identity_oidc_application WHERE ClientId='native-seed-client'"));
        Assert.AreEqual(retainedSecret, await db.QuerySingleAsync<string>("SELECT ClientSecret FROM fn_identity_oidc_application WHERE ClientId='native-seed-client'"));

        async Task SuccessfulAsync(string profile)
        {
            await using var process = NativeIndependentHostProcess.Start(executable, ["migrate", "--seed", profile], environment, "migrator-" + profile);
            using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(8));
            var result = await process.WaitAsync(limit.Token);
            Assert.AreEqual(0, result.ExitCode, result.Errors + result.Output);
            Assert.IsFalse(result.Output.Contains("NATIVE_ROLE_LOG_TRUNCATED", StringComparison.Ordinal));
            Assert.IsFalse(result.Errors.Contains("NATIVE_ROLE_LOG_TRUNCATED", StringComparison.Ordinal));
        }
        async Task<int[]> SnapshotAsync() =>
        [
            await db.QuerySingleAsync<int>($"SELECT COUNT(*) FROM {journal}"),
            await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_identity_user WHERE NormalizedUsername='NATIVE-ADMIN'"),
            await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_identity_role"),
            await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_identity_navigation"),
            await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_settings_dict_type"),
            await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_settings_dict_item")
        ];
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Native_migrator_stops_before_seed_when_journal_is_invalid(DatabaseProvider provider)
    {
        var executable = NativeIndependentHostProcess.RequireArtifact("migrator");
        var connectionString = await CreateDatabaseAsync(provider);
        await using var db = Connection(provider, connectionString);
        await db.OpenAsync();
        // 独享空库仅注入不合法 journal：读取或后续写入必须失败，不能继续任何播种。
        var journal = provider == DatabaseProvider.SqlServer ? "dbo.SchemaVersions" : "schemaversions";
        await db.ExecuteAsync($"CREATE TABLE {journal} (Id int NOT NULL PRIMARY KEY, ScriptName int NOT NULL, Applied datetime NOT NULL)");
        await db.ExecuteAsync($"INSERT INTO {journal} (Id,ScriptName,Applied) VALUES (1,42,CURRENT_TIMESTAMP)");
        await using var process = NativeIndependentHostProcess.Start(executable, ["migrate", "--seed", "baseline"], EnvironmentFor(provider, connectionString), "migrator-invalid-journal");
        using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var result = await process.WaitAsync(limit.Token);
        Assert.AreEqual(1, result.ExitCode, result.Errors + result.Output);
        StringAssert.Contains(result.Errors, "migrator.migration.failed");
        foreach (var table in new[] { "fn_seed_run", "fn_identity_user" })
        {
            var exists = provider == DatabaseProvider.SqlServer
                ? "SELECT COUNT(*) FROM sys.tables WHERE name=@Table"
                : "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@Table";
            if (await db.QuerySingleAsync<int>(exists, new { Table = table }) != 0)
                Assert.AreEqual(0, await db.QuerySingleAsync<int>($"SELECT COUNT(*) FROM {table}"), "部分 DDL 可存在，但不能已执行任何播种。");
        }
        Assert.AreEqual(1, await db.QuerySingleAsync<int>($"SELECT COUNT(*) FROM {journal}"));
    }

    private static Task<string> CreateDatabaseAsync(DatabaseProvider provider) => provider == DatabaseProvider.SqlServer
        ? SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : SharedDatabaseFixture.CreateMySqlDatabaseAsync();
    private static DbConnection Connection(DatabaseProvider provider, string connectionString) => provider == DatabaseProvider.SqlServer
        ? new SqlConnection(connectionString) : new MySqlConnection(connectionString);

    private static Dictionary<string, string?> EnvironmentFor(DatabaseProvider provider, string connectionString) => new()
    {
        ["DOTNET_ENVIRONMENT"] = "Testing", ["Database__Provider"] = provider.ToString(),
        ["Database__ConnectionString"] = connectionString, ["Database__MySqlGuidStorageMode"] = "Binary16",
        ["Database__CommandTimeoutSeconds"] = "300", ["Identity__EnableTokenEndpoints"] = "false",
        ["Identity__AllowDevelopmentEphemeralSigningKey"] = "false", ["Identity__Oidc__Enable"] = "false",
        ["Identity__EnableRemoteSuperAdministratorManagement"] = "false",
        ["Identity__Bootstrap__Username"] = "native-admin", ["Identity__Bootstrap__Password"] = "Native-Migrator-TestOnly-!2026",
        ["UuidBinaryContract__MaintenanceMode"] = "true", ["UuidBinaryContract__BackupVerified"] = "true",
        ["UuidBinaryContract__LegacyWritersStopped"] = "true", ["UuidBinaryContract__DestructiveDdlApprovalId"] = MigrationContractOptionFactory.UuidApprovalId,
        ["PreV1NamingContract__MaintenanceMode"] = "true", ["PreV1NamingContract__BackupVerified"] = "true",
        ["PreV1NamingContract__LegacyWritersStopped"] = "true", ["PreV1NamingContract__LegacyOutboxDrained"] = "true",
        ["PreV1NamingContract__DestructiveDdlApprovalId"] = MigrationContractOptionFactory.NamingApprovalId
    };
}
