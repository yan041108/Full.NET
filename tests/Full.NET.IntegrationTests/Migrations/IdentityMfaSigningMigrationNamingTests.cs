using System.Data.Common;
using Dapper;
using DbUp;
using DbUp.Helpers;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>只执行 301/303，验证静态 DDL 的初次安装、未记账重入及安全状态和主键约束保留。</summary>
[TestClass]
public sealed class IdentityMfaSigningMigrationNamingTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Static_migrations_recover_without_resetting_totp_or_signing_state(DatabaseProvider provider)
    {
        var connectionString = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var normalized = provider == DatabaseProvider.MySql
            ? MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: true)
            : connectionString;
        await using DbConnection db = provider == DatabaseProvider.SqlServer
            ? new SqlConnection(normalized) : new MySqlConnection(normalized);
        await db.OpenAsync();
        await db.ExecuteAsync(provider == DatabaseProvider.SqlServer
            ? "CREATE TABLE dbo.fn_identity_user_totp (UserId uniqueidentifier NOT NULL CONSTRAINT PK_fn_identity_user_totp PRIMARY KEY CLUSTERED)"
            : "CREATE TABLE fn_identity_user_totp (UserId binary(16) NOT NULL, CONSTRAINT PK_fn_identity_user_totp PRIMARY KEY (UserId))");

        Replay(provider, normalized, "301_IdentityOidcMfaCounter.sql", "303_IdentityOidcKeyGovernance.sql");
        var userId = Guid.CreateVersion7();
        await db.ExecuteAsync("INSERT INTO fn_identity_user_totp (UserId, LastAcceptedCounter) VALUES (@userId, 100)", new { userId });
        var keyId = Guid.CreateVersion7();
        var now = DateTime.UtcNow;
        var key = new { Id = keyId, KeyId = "retired-key", Hash = new string('a', 64), Now = now };
        const string insertKey = """
            INSERT INTO fn_identity_oidc_signing_key
                (Id, KeyId, PublicKeyHash, PublishedAtUtc, LastActivatedAtUtc, RetireAfterUtc, IsRetired, Version)
            VALUES (@Id, @KeyId, @Hash, @Now, @Now, @Now, 1, 17)
            """;
        await db.ExecuteAsync(insertKey, key);

        // 模拟首张表已提交、第二张表缺失且 DbUp 尚未记账；保留已有退役记录再补齐缺项。
        await db.ExecuteAsync("DROP TABLE fn_identity_oidc_signing_node");
        Replay(provider, normalized, "301_IdentityOidcMfaCounter.sql", "303_IdentityOidcKeyGovernance.sql");
        var node = new { Id = Guid.CreateVersion7(), Hash = new string('b', 64), Now = now, Expires = now.AddMinutes(1) };
        const string insertNode = """
            INSERT INTO fn_identity_oidc_signing_node
                (Id, RoleKey, SigningManifest, EncryptionFingerprint, PrivateKeysJson, PolicyVersion, ExpiresAtUtc, UpdatedAtUtc)
            VALUES (@Id, 'api', @Hash, @Hash, '[]', 13, @Expires, @Now)
            """;
        await db.ExecuteAsync(insertNode, node);
        Replay(provider, normalized, "301_IdentityOidcMfaCounter.sql", "303_IdentityOidcKeyGovernance.sql");

        Assert.AreEqual(100L, await db.QuerySingleAsync<long>("SELECT LastAcceptedCounter FROM fn_identity_user_totp WHERE UserId=@userId", new { userId }));
        Assert.AreEqual(17, await db.QuerySingleAsync<int>("SELECT Version FROM fn_identity_oidc_signing_key WHERE Id=@keyId AND IsRetired=1", new { keyId }));
        Assert.AreEqual(13, await db.QuerySingleAsync<int>("SELECT PolicyVersion FROM fn_identity_oidc_signing_node WHERE Id=@Id", node));
        await Assert.ThrowsAsync<DbException>(() => db.ExecuteAsync(insertKey, key));
        await Assert.ThrowsAsync<DbException>(() => db.ExecuteAsync(insertNode, node));
        await Assert.ThrowsAsync<DbException>(() => db.ExecuteAsync(insertKey,
            new { Id = Guid.CreateVersion7(), KeyId = "retired-key", Hash = new string('a', 64), Now = now }));
        if (provider == DatabaseProvider.MySql)
            Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM information_schema.ROUTINES WHERE ROUTINE_SCHEMA=DATABASE() AND ROUTINE_NAME='fn_identity_ensure_mfa_counter'"));
    }

    /// <summary>精确匹配文件名并禁用记账，确保每次都执行目标 DDL，不随其他同编号迁移扩围。</summary>
    internal static void Replay(DatabaseProvider provider, string connectionString, params string[] scriptNames)
    {
        var builder = provider == DatabaseProvider.SqlServer
            ? DeployChanges.To.SqlDatabase(connectionString)
            : DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(
                connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: true));
        var result = builder.WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly,
                name => name.Contains($".Migrations.{provider}.", StringComparison.Ordinal)
                    && scriptNames.Any(script => name.EndsWith("." + script, StringComparison.Ordinal)))
            .JournalTo(new NullJournal()).WithExecutionTimeout(TimeSpan.FromSeconds(120)).Build().PerformUpgrade();
        Assert.IsTrue(result.Successful, result.Error?.ToString());
        Assert.HasCount(scriptNames.Length, result.Scripts);
    }
}
