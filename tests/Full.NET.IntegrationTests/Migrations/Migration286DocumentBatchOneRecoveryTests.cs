using System.Data.Common;
using System.Text.Json;
using Dapper;
using DbUp;
using DbUp.Helpers;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证文档与上传幂等迁移在部分 DDL 已提交、DbUp 未记账时可恢复并重复执行。</summary>
[TestClass]
public sealed class Migration286DocumentBatchOneRecoveryTests
{
    [TestMethod]
    public Task SqlServer_recovers_partial_document_columns_and_upload_indexes() => VerifyAsync(true);
    [TestMethod]
    public Task MySql_recovers_partial_document_columns_and_upload_indexes() => VerifyAsync(false);

    private static async Task VerifyAsync(bool sqlServer)
    {
        var provider = sqlServer ? DatabaseProvider.SqlServer : DatabaseProvider.MySql;
        var connectionString = sqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await MigrateThrough285Async(provider, connectionString);
        await using DbConnection connection = sqlServer ? new SqlConnection(connectionString)
            : ReviewFixMigrationRecoverySupport.MySqlConnection(connectionString);
        var documentId = Guid.CreateVersion7();
        var subjectId = Guid.CreateVersion7();
        await connection.ExecuteAsync("""
            INSERT INTO fn_document_item (Id, Title, CreatedAtUtc, CreatedByUserId)
            VALUES (@DocumentId, 'Recovery evidence', @Now, @SubjectId);
            INSERT INTO fn_document_permission (Id, DocumentId, UserId, PermissionLevel, CreatedAtUtc)
            VALUES (@PermissionId, @DocumentId, @SubjectId, 'write', @Now);
            """, new { DocumentId = documentId, SubjectId = subjectId, PermissionId = Guid.CreateVersion7(), Now = DateTimeOffset.UtcNow });
        // 模拟首个 DDL 已提交但迁移尚未记账，后续步骤应依据结构探测收敛。
        await connection.ExecuteAsync(sqlServer
            ? "ALTER TABLE fn_document_item ADD VisibilityMode varchar(16) NOT NULL DEFAULT 'host'"
            : "ALTER TABLE fn_document_item ADD COLUMN VisibilityMode varchar(16) NOT NULL DEFAULT 'host' COMMENT '文档可见模式，host 或 restricted'");
        for (var run = 0; run < 2; run++)
        {
            var builder = sqlServer ? DeployChanges.To.SqlDatabase(connectionString)
                : DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(connectionString,
                    MySqlGuidStorageMode.Binary16, allowUserVariables: true));
            var result = builder.WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly,
                    name => name.Contains(sqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.", StringComparison.Ordinal)
                        && (name.EndsWith("286_DocumentAccessLifecycle.sql", StringComparison.Ordinal)
                            || name.EndsWith("287_FilesUploadOperation.sql", StringComparison.Ordinal)))
                .JournalTo(new NullJournal()).WithExecutionTimeout(TimeSpan.FromSeconds(300)).Build().PerformUpgrade();
            Assert.IsTrue(result.Successful, result.Error?.ToString());
            Assert.HasCount(2, result.Scripts);
        }
        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>(sqlServer
            ? "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE COLUMN_NAME='PermissionLevel' AND TABLE_NAME='fn_document_permission'"
            : "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND COLUMN_NAME='PermissionLevel' AND TABLE_NAME='fn_document_permission'"));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(sqlServer
            ? "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_files_file') AND name=N'UX_fn_files_file_UploadOperationKey'"
            : "SELECT COUNT(DISTINCT INDEX_NAME) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_files_file' AND INDEX_NAME='UX_fn_files_file_UploadOperationKey'"));
        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_document_share_session"));
        Assert.AreEqual(3, await connection.ExecuteScalarAsync<int>(
            "SELECT Actions FROM fn_document_permission WHERE DocumentId=@DocumentId AND UserId=@SubjectId",
            new { DocumentId = documentId, SubjectId = subjectId }));
        Assert.AreEqual("restricted", await connection.ExecuteScalarAsync<string>(
            "SELECT VisibilityMode FROM fn_document_item WHERE Id=@Id", new { Id = documentId }));
    }

    private static async Task MigrateThrough285Async(DatabaseProvider provider, string connectionString)
    {
        var segment = provider == DatabaseProvider.SqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.";
        var names = typeof(DbUpMigrationRunner).Assembly.GetManifestResourceNames()
            .Where(name => name.Contains(segment, StringComparison.Ordinal)
                && NamingExpandTestMigrationRunner.IsThroughMigration(name, 285)).ToArray();
        var workspace = Directory.CreateTempSubdirectory("fullnet-document-through285-").FullName;
        try
        {
            var scripts = names.Select(name => new { name = name[(name.IndexOf(segment, StringComparison.Ordinal) + segment.Length)..] });
            File.WriteAllText(Path.Combine(workspace, "framework-manifest.json"), JsonSerializer.Serialize(new
            {
                migrationInventory = new { selectionStatus = "preset-document-through285", scripts },
            }));
            var runner = new DbUpMigrationRunner(Options.Create(new DatabaseOptions
                {
                    Provider = provider, ConnectionString = connectionString,
                    MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16, CommandTimeoutSeconds = 300,
                }), NullLoggerFactory.Instance,
                MigrationContractOptionFactory.UuidOptions(), MigrationContractOptionFactory.NamingOptions(),
                Options.Create(new FrameworkManifestMigrationOptions { ContentRoot = workspace }));
            var result = await runner.MigrateAsync();
            Assert.IsTrue(result.Successful);
            Assert.AreEqual(names.Length, result.ExecutedScriptCount);
        }
        finally
        {
            // 仅删除本测试刚创建且路径已验证的临时目录。
            Assert.IsTrue(Path.GetFullPath(workspace).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase));
            Directory.Delete(workspace, recursive: true);
        }
    }
}
