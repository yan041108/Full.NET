using System.Data.Common;
using System.Text.RegularExpressions;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>来源列与索引各自恢复，保留旧成员操作和新注册来源。</summary>
[TestClass]
public sealed class PublicRegistrationEnrollmentMigrationRecoveryTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Enrollment_migration_recovers_partial_ddl_and_preserves_nullable_source(DatabaseProvider provider)
    {
        var server = provider == DatabaseProvider.SqlServer;
        var connectionString = server ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await using DbConnection connection = server ? new SqlConnection(connectionString) : ReviewFixMigrationRecoverySupport.MySqlConnection(connectionString);
        await connection.OpenAsync();
        await RunAsync("324_IdentityMemberSeatOperations.sql");
        var id = Guid.CreateVersion7();
        await connection.ExecuteAsync("""
            INSERT INTO fn_identity_member_seat_operation
              (Id,RequestKey,TenantId,UserId,MemberId,MemberVersion,Phase,CreatedAtUtc,DueAtUtc,Version,Attempt)
            VALUES (@Id,'old-registration',@TenantId,@UserId,@MemberId,1,'Requested',@Now,@Now,1,1)
            """, new { Id = id, TenantId = Guid.CreateVersion7(), UserId = Guid.CreateVersion7(), MemberId = Guid.CreateVersion7(), Now = DateTimeOffset.UtcNow });
        await RunAsync("361_IdentityPublicRegistrationEnrollment.sql");
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM fn_identity_member_seat_operation WHERE Id=@Id AND RegistrationWayId IS NULL AND Version=1", new { Id = id }));
        var way = Guid.CreateVersion7();
        await connection.ExecuteAsync("UPDATE fn_identity_member_seat_operation SET RegistrationWayId=@Way WHERE Id=@Id", new { Way = way, Id = id });
        await connection.ExecuteAsync(server ? "DROP INDEX IX_fn_identity_member_seat_operation_User_Created ON fn_identity_member_seat_operation"
            : "ALTER TABLE fn_identity_member_seat_operation DROP INDEX IX_fn_identity_member_seat_operation_User_Created");
        await RunAsync("361_IdentityPublicRegistrationEnrollment.sql");
        await RunAsync("361_IdentityPublicRegistrationEnrollment.sql");
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM fn_identity_member_seat_operation WHERE Id=@Id AND RegistrationWayId=@Way AND Version=1", new { Id = id, Way = way }));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(server
            ? "SELECT COUNT(1) FROM sys.indexes WHERE object_id=OBJECT_ID('fn_identity_member_seat_operation') AND name='IX_fn_identity_member_seat_operation_User_Created'"
            : "SELECT COUNT(DISTINCT INDEX_NAME) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_identity_member_seat_operation' AND INDEX_NAME='IX_fn_identity_member_seat_operation_User_Created'"));
        async Task RunAsync(string name)
        {
            var assembly = typeof(DbUpMigrationRunner).Assembly;
            using var reader = new StreamReader(assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(resource => resource.EndsWith($"{(server ? "SqlServer" : "MySql")}.{name}", StringComparison.Ordinal)))!);
            var script = await reader.ReadToEndAsync();
            if (!server) script = Regex.Replace(script, @"^DELIMITER[^\r\n]*", string.Empty, RegexOptions.Multiline).Replace("END$$", "END;", StringComparison.Ordinal);
            foreach (var batch in (server ? Regex.Split(script, @"^GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase) : [script]).Where(value => !string.IsNullOrWhiteSpace(value)))
                await connection.ExecuteAsync(batch);
        }
    }
}
