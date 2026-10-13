using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>媒体代际列展开可恢复部分 DDL，不回填旧引用或覆盖已写入的代际。</summary>
[TestClass]
public sealed class Migration252MediaBindingClaimGenerationRecoveryTests
{
    [TestMethod]
    public Task SqlServer_recovers_partial_and_unjournaled_columns() => VerifyAsync(DatabaseProvider.SqlServer);

    [TestMethod]
    public Task MySql_recovers_partial_and_unjournaled_columns() => VerifyAsync(DatabaseProvider.MySql);

    private static async Task VerifyAsync(DatabaseProvider provider)
    {
        var cs = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var migration = new Through252Scope(provider, cs);
        await migration.Runner.MigrateAsync();
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(cs)
            : ReviewFixMigrationRecoverySupport.MySqlConnection(cs);
        await db.OpenAsync();
        var user = Guid.CreateVersion7();
        object now = provider == DatabaseProvider.MySql ? DateTime.UtcNow : DateTimeOffset.UtcNow;
        await db.ExecuteAsync("""
            INSERT INTO fn_identity_user (Id, TenantId, ScopeKey, Username, NormalizedUsername, DisplayName, PasswordHash, SecurityStamp, IsActive, CreatedAtUtc)
            VALUES (@User, NULL, 'host', @Name, @Name, 'migration media', 'unused', 'migration-media', 1, @Now)
            """, new { User = user, Name = "migration-media-" + user.ToString("N"), Now = now });
        var file = Guid.CreateVersion7(); var key = "migration-media-generation";
        await db.ExecuteAsync("""
            INSERT INTO fn_identity_user_profile (UserId, AvatarFileId, AvatarClaimKey, Version)
            VALUES (@User, @File, @Key, 7)
            """, new { User = user, File = file, Key = key });

        // 首列已提交而后两列尚未执行，且 DbUp 未记账；已存在的业务代际必须原样保留。
        await db.ExecuteAsync("ALTER TABLE fn_identity_user_profile DROP COLUMN SignatureClaimKey");
        await db.ExecuteAsync("ALTER TABLE fn_tenancy_tenant DROP COLUMN LogoClaimKey");
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(db, "252_MediaBindingClaimGeneration.sql", provider == DatabaseProvider.SqlServer);
        Assert.AreEqual(1, (await migration.Runner.MigrateAsync()).ExecutedScriptCount);
        Assert.AreEqual(0, (await migration.Runner.MigrateAsync()).ExecutedScriptCount);
        var profile = await db.QuerySingleAsync<Profile>("SELECT AvatarFileId, AvatarClaimKey, SignatureClaimKey, Version FROM fn_identity_user_profile WHERE UserId = @User", new { User = user });
        Assert.AreEqual(file, profile.AvatarFileId); Assert.AreEqual(key, profile.AvatarClaimKey);
        Assert.IsNull(profile.SignatureClaimKey); Assert.AreEqual(7, profile.Version);
        Assert.AreEqual(3L, await db.ExecuteScalarAsync<long>("""
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_NAME IN ('fn_identity_user_profile', 'fn_tenancy_tenant')
              AND TABLE_SCHEMA = @Schema
              AND COLUMN_NAME IN ('AvatarClaimKey', 'SignatureClaimKey', 'LogoClaimKey')
              AND IS_NULLABLE = 'YES' AND CHARACTER_MAXIMUM_LENGTH = 200
            """, new { Schema = provider == DatabaseProvider.MySql ? db.Database : "dbo" }));
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(db, "252_MediaBindingClaimGeneration.sql", provider == DatabaseProvider.SqlServer);
        Assert.AreEqual(1, (await migration.Runner.MigrateAsync()).ExecutedScriptCount);
        Assert.AreEqual(0, (await migration.Runner.MigrateAsync()).ExecutedScriptCount);
        Assert.AreEqual(key, await db.QuerySingleAsync<string>("SELECT AvatarClaimKey FROM fn_identity_user_profile WHERE UserId = @User", new { User = user }));
    }

    private sealed class Profile
    {
        public Guid AvatarFileId { get; set; }
        public string AvatarClaimKey { get; set; } = "";
        public string? SignatureClaimKey { get; set; }
        public int Version { get; set; }
    }

    // 冻结迁移上界，后续升级不能改变本用例的部分恢复语义。
    private sealed class Through252Scope : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("fullnet-media252-").FullName;
        internal DbUpMigrationRunner Runner { get; }
        internal Through252Scope(DatabaseProvider provider, string cs)
        {
            var segment = provider == DatabaseProvider.SqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.";
            var names = typeof(DbUpMigrationRunner).Assembly.GetManifestResourceNames()
                .Where(name => name.Contains(segment, StringComparison.Ordinal) && NamingExpandTestMigrationRunner.IsThroughMigration(name, 252)).ToArray();
            Assert.IsTrue(names.Any(name => name.EndsWith("252_MediaBindingClaimGeneration.sql", StringComparison.Ordinal)));
            File.WriteAllText(Path.Combine(directory, "framework-manifest.json"), System.Text.Json.JsonSerializer.Serialize(new {
                migrationInventory = new { selectionStatus = "preset-recovery-through252", scripts = names.Select(name => new { name = name[(name.IndexOf(segment, StringComparison.Ordinal) + segment.Length)..] }) } }));
            Runner = new(Options.Create(new DatabaseOptions { Provider = provider, ConnectionString = cs,
                    MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16, CommandTimeoutSeconds = 300 }),
                Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, MigrationContractOptionFactory.UuidOptions(), MigrationContractOptionFactory.NamingOptions(),
                Options.Create(new FrameworkManifestMigrationOptions { ContentRoot = directory }));
        }
        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}
