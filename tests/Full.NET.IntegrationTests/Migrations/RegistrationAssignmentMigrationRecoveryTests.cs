using System.Data.Common;
using System.Text.RegularExpressions;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>直接执行本批迁移，验证未记账半完成及重复部署保留原有恢复意图。</summary>
[TestClass]
public sealed class RegistrationAssignmentMigrationRecoveryTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Registration_assignment_migration_recovers_without_rewriting_intents(DatabaseProvider provider)
    {
        var server = provider == DatabaseProvider.SqlServer;
        var connectionString = server ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await using DbConnection connection = server ? new SqlConnection(connectionString) : ReviewFixMigrationRecoverySupport.MySqlConnection(connectionString);
        await connection.OpenAsync();
        var assembly = typeof(DbUpMigrationRunner).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(name => name.EndsWith($"{(server ? "SqlServer" : "MySql")}.360_OrganizationRegistrationAssignments.sql", StringComparison.Ordinal));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
        var script = await reader.ReadToEndAsync();
        var batches = server ? Regex.Split(script, @"^GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase) : [script];
        // SQL Server 模拟只提交建表；MySQL 的单条原子建表同时建立索引。
        await connection.ExecuteAsync(batches[0]);
        var id = Guid.CreateVersion7();
        await connection.ExecuteAsync("""
            INSERT INTO fn_organization_registration_assignment
              (Id,TenantId,MemberId,MemberVersion,UserId,UnitId,PositionId,Status,ErrorCode,CreatedAtUtc,DueAtUtc,Version)
            VALUES (@Id,@TenantId,@MemberId,2,@UserId,@UnitId,NULL,'Pending',NULL,@Now,@Now,1)
            """, new { Id = id, TenantId = Guid.CreateVersion7(), MemberId = Guid.CreateVersion7(),
                UserId = Guid.CreateVersion7(), UnitId = Guid.CreateVersion7(), Now = DateTimeOffset.UtcNow });
        for (var repeat = 0; repeat < 2; repeat++)
            foreach (var batch in batches.Where(value => !string.IsNullOrWhiteSpace(value))) await connection.ExecuteAsync(batch);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM fn_organization_registration_assignment WHERE Id=@Id AND Status='Pending' AND Version=1", new { Id = id }));
        var indexCount = await connection.ExecuteScalarAsync<int>(server
            ? "SELECT COUNT(1) FROM sys.indexes WHERE object_id=OBJECT_ID('fn_organization_registration_assignment') AND name='IX_fn_organization_registration_assignment_Due'"
            : "SELECT COUNT(DISTINCT INDEX_NAME) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_organization_registration_assignment' AND INDEX_NAME='IX_fn_organization_registration_assignment_Due'");
        Assert.AreEqual(1, indexCount);
    }
}
