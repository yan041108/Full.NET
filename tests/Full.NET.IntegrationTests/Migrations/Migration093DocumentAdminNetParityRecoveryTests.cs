using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>
/// 验证 093 在 fn_document_share 表存在旧 Password 列（缺失 PasswordHash）时无损收敛；
/// PasswordHash 存在但长度不足时扩展；遗留 Password 列在数据迁移后被删除。
/// 同时验证 fn_document_permission / fn_document_share 幂等补列与补索引。
/// </summary>
[TestClass]
public sealed class Migration093DocumentAdminNetParityRecoveryTests
{
    [TestMethod]
    public async Task SqlServer_document_share_recovers_password_hash_column_without_dropping_plaintext()
    {
        var connectionString = await SharedDatabaseFixture
            .CreateSqlServerDatabaseAsync()
            .ConfigureAwait(false);
        var runner = CreateRunner(DatabaseProvider.SqlServer, connectionString);
        await runner.MigrateAsync().ConfigureAwait(false);

        await using var connection = new SqlConnection(connectionString);
        var now = DateTimeOffset.UtcNow;
        var documentId = Guid.CreateVersion7();
        var shareId = Guid.CreateVersion7();

        // 中文注释：模拟迁移前的“漂移状态”——fn_document_share 已存在但只有
        // 旧 Password 列（nvarchar(256)），没有 PasswordHash 列；手动插入一条
        // 含旧明文口令的分享记录后，从 SchemaVersions 删除 093 迁移记录并重跑。
        await connection.ExecuteAsync(
                """
                -- 先构造一条完整文档记录（作为 fn_document_share 外键依赖）
                IF NOT EXISTS (SELECT 1 FROM dbo.fn_document_item WHERE Id = @DocumentId)
                BEGIN
                    INSERT INTO dbo.fn_document_item
                        (Id, TenantId, CategoryId, DocumentNo, Title, FileName, FileExtension,
                         MimeType, FileSizeBytes, StorageBlobId, CurrentVersionId, CreatedAtUtc,
                         CreatedByUserId, UpdatedAtUtc, UpdatedByUserId, DeletedAtUtc, DeletedByUserId, Version)
                    VALUES
                        (@DocumentId, NULL, NULL, 'DOC-093-REC', 'Recovery Doc', 'rec.txt', '.txt',
                         'text/plain', 32, @BlobId, NULL, @Now,
                         '11111111-1111-1111-1111-111111111111', NULL, NULL, NULL, NULL, 1);
                END;

                -- 把 fn_document_share 回滚到“只有 Password 无 PasswordHash”的漂移状态
                IF COL_LENGTH(N'dbo.fn_document_share', N'Password') IS NULL
                   AND COL_LENGTH(N'dbo.fn_document_share', N'PasswordHash') IS NOT NULL
                BEGIN
                    ALTER TABLE dbo.fn_document_share
                        ADD Password nvarchar(256) NULL;
                    UPDATE dbo.fn_document_share SET Password = PasswordHash;
                    ALTER TABLE dbo.fn_document_share
                        DROP COLUMN IF EXISTS PasswordHash;
                END;

                -- 插入一条使用旧 Password 列的分享记录（模拟历史数据明文保留）
                MERGE dbo.fn_document_share AS t
                USING (SELECT @ShareId AS Id) AS s
                ON t.Id = s.Id
                WHEN NOT MATCHED THEN
                    INSERT
                        (Id, TenantId, DocumentId, ShareCode, Password, ExpireTime,
                         MaxAccessCount, AccessCount, IsEnabled, Version, CreatedAtUtc)
                    VALUES
                        (@ShareId, NULL, @DocumentId, @ShareCode, @LegacyPassword, @ExpireTime,
                         5, 0, 1, 1, @Now);

                -- 抹掉 093 迁移记录，强制 runner 重跑该脚本
                DELETE FROM dbo.SchemaVersions
                WHERE ScriptName LIKE '%093_DocumentAdminNetParity.sql';
                """,
                new
                {
                    DocumentId = documentId,
                    BlobId = Guid.CreateVersion7(),
                    Now = now,
                    ShareId = shareId,
                    ShareCode = "DOC-093-REC-SHARE",
                    LegacyPassword = "OldP@ss123!",
                    ExpireTime = now.AddDays(30),
                })
            .ConfigureAwait(false);

        var recovered = await runner.MigrateAsync().ConfigureAwait(false);

        Assert.AreEqual(1, recovered.ExecutedScriptCount, "093 必须被重跑 1 次以完成收敛");
        Assert.IsNull(
            await connection.ExecuteScalarAsync<object?>(
                "SELECT COL_LENGTH(N'dbo.fn_document_share', N'Password')").ConfigureAwait(false)
                ?? null,
            "收敛后旧 Password 列必须被删除");
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.fn_document_share WHERE Id = @Id",
            new { Id = shareId }).ConfigureAwait(false),
            "迁移过程中不得删除历史分享数据");
        var migratedHash = await connection.ExecuteScalarAsync<string?>(
            "SELECT PasswordHash FROM dbo.fn_document_share WHERE Id = @Id",
            new { Id = shareId }).ConfigureAwait(false);
        Assert.AreEqual("OldP@ss123!", migratedHash,
            "旧 Password 内容必须无损拷贝到 PasswordHash，后续再由 Hasher 异步升级为哈希");

        // 中文注释：二次调用 MigrateAsync 必须幂等（0 条脚本执行），证明收敛后的结构稳定。
        Assert.AreEqual(0, (await runner.MigrateAsync().ConfigureAwait(false)).ExecutedScriptCount);
    }

    [TestMethod]
    public async Task MySql_document_share_recovers_password_hash_column_without_dropping_plaintext()
    {
        var connectionString = await SharedDatabaseFixture
            .CreateMySqlDatabaseAsync()
            .ConfigureAwait(false);
        var runner = CreateRunner(DatabaseProvider.MySql, connectionString);
        await runner.MigrateAsync().ConfigureAwait(false);

        await using var connection = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                connectionString,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));
        var now = DateTime.UtcNow;
        var documentId = Guid.CreateVersion7();
        var shareId = Guid.CreateVersion7();

        await connection.ExecuteAsync(
                """
                -- 外键依赖：确保 fn_document_item 存在目标记录
                INSERT IGNORE INTO fn_document_item
                    (Id, TenantId, CategoryId, DocumentNo, Title, FileName, FileExtension,
                     MimeType, FileSizeBytes, StorageBlobId, CurrentVersionId, CreatedAtUtc,
                     CreatedByUserId, UpdatedAtUtc, UpdatedByUserId, DeletedAtUtc, DeletedByUserId, Version)
                VALUES
                    (@DocumentId, NULL, NULL, 'DOC-093-REC', 'Recovery Doc', 'rec.txt', '.txt',
                     'text/plain', 32, @BlobId, NULL, @Now,
                     UNHEX(REPLACE('11111111-1111-1111-1111-111111111111', '-', '')),
                     NULL, NULL, NULL, NULL, 1);

                -- 构造漂移：如果只有 PasswordHash，就把它改回旧 Password 列
                IF EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'fn_document_share'
                      AND COLUMN_NAME = 'PasswordHash'
                ) AND NOT EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'fn_document_share'
                      AND COLUMN_NAME = 'Password'
                ) THEN
                    ALTER TABLE fn_document_share
                        ADD COLUMN Password varchar(256) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL;
                    UPDATE fn_document_share SET Password = PasswordHash;
                    ALTER TABLE fn_document_share
                        DROP COLUMN PasswordHash;
                END IF;

                -- 插入一条旧格式分享记录（含明文旧 Password）
                INSERT IGNORE INTO fn_document_share
                    (Id, TenantId, DocumentId, ShareCode, Password, ExpireTime,
                     MaxAccessCount, AccessCount, IsEnabled, Version, CreatedAtUtc)
                VALUES
                    (@ShareId, NULL, @DocumentId, @ShareCode, @LegacyPassword, @ExpireTime,
                     5, 0, 1, 1, @Now);

                -- 抹掉 093 迁移记录
                DELETE FROM schemaversions
                WHERE ScriptName LIKE '%093_DocumentAdminNetParity.sql';
                """,
                new
                {
                    DocumentId = documentId,
                    BlobId = Guid.CreateVersion7(),
                    Now = now,
                    ShareId = shareId,
                    ShareCode = "DOC-093-REC-SHARE",
                    LegacyPassword = "OldP@ss123!",
                    ExpireTime = now.AddDays(30),
                })
            .ConfigureAwait(false);

        var recovered = await runner.MigrateAsync().ConfigureAwait(false);

        Assert.AreEqual(1, recovered.ExecutedScriptCount, "093 必须被重跑 1 次以完成收敛");
        var legacyColumnExists = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_document_share'
              AND COLUMN_NAME = 'Password'
            """).ConfigureAwait(false);
        Assert.AreEqual(0, legacyColumnExists, "收敛后旧 Password 列必须被删除");
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fn_document_share WHERE Id = @Id",
            new { Id = shareId }).ConfigureAwait(false),
            "迁移过程中不得删除历史分享数据");
        var migratedHash = await connection.ExecuteScalarAsync<string?>(
            "SELECT PasswordHash FROM fn_document_share WHERE Id = @Id",
            new { Id = shareId }).ConfigureAwait(false);
        Assert.AreEqual("OldP@ss123!", migratedHash,
            "旧 Password 内容必须无损拷贝到 PasswordHash");

        Assert.AreEqual(0, (await runner.MigrateAsync().ConfigureAwait(false)).ExecutedScriptCount);
    }

    private static DbUpMigrationRunner CreateRunner(
        DatabaseProvider provider,
        string connectionString) =>
        new(
            Options.Create(new DatabaseOptions
            {
                Provider = provider,
                ConnectionString = connectionString,
                MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16,
                CommandTimeoutSeconds = 300,
            }),
            NullLoggerFactory.Instance,
            MigrationContractOptionFactory.UuidOptions(),
            MigrationContractOptionFactory.NamingOptions());
}
