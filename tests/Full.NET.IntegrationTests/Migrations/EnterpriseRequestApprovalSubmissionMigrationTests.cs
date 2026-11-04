using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Microsoft.Data.SqlClient;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>双库验证提交日志回滚、单请求唯一绑定与建表中断后的恢复。</summary>
[TestClass]
public sealed class EnterpriseRequestApprovalSubmissionMigrationTests
{
    [TestMethod]
    public Task SqlServer_submission_atomicity_and_reentry() => VerifyAsync(DatabaseProvider.SqlServer);
    [TestMethod]
    public Task MySql_submission_atomicity_and_reentry() => VerifyAsync(DatabaseProvider.MySql);

    private static async Task VerifyAsync(DatabaseProvider provider)
    {
        var cs = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        SampleMigrationRecoverySupport.Run(provider, cs, "005_EnterpriseRequestApprovalSubmission.sql");
        await using DbConnection connection = provider == DatabaseProvider.SqlServer ? new SqlConnection(cs)
            : ReviewFixMigrationRecoverySupport.MySqlConnection(cs);
        await connection.OpenAsync();
        var tenant = Guid.CreateVersion7(); var request = Guid.CreateVersion7(); var instance = Guid.CreateVersion7();
        const string insert = """
            INSERT INTO demo_enterprise_request_approval_submission
                (Id, TenantId, RequestId, RequestVersion, WorkflowDefinitionVersionId, WorkflowInstanceId,
                 SubmittedById, OrganizationUnitId, BusinessTitle, CreatedAtUtc)
            VALUES (@Id, @TenantId, @RequestId, 2, @DefinitionId, @InstanceId, @ActorId, @UnitId, 'Draft snapshot', @Now)
            """;
        var parameters = new { Id = Guid.CreateVersion7(), TenantId = tenant, RequestId = request,
            DefinitionId = Guid.CreateVersion7(), InstanceId = instance, ActorId = Guid.CreateVersion7(), UnitId = Guid.CreateVersion7(), Now = DateTime.UtcNow };
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await connection.ExecuteAsync(insert, parameters, transaction); await transaction.RollbackAsync();
        }
        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM demo_enterprise_request_approval_submission"));
        await connection.ExecuteAsync(insert, parameters);
        await Assert.ThrowsAsync<DbException>(async () => await connection.ExecuteAsync(insert, new { Id = Guid.CreateVersion7(), TenantId = tenant,
            RequestId = request, DefinitionId = parameters.DefinitionId, InstanceId = Guid.CreateVersion7(),
            parameters.ActorId, parameters.UnitId, parameters.Now }));
        if (provider == DatabaseProvider.SqlServer)
            await connection.ExecuteAsync("DROP INDEX UX_demo_enterprise_request_approval_submission_Instance ON dbo.demo_enterprise_request_approval_submission");
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(connection, "005_EnterpriseRequestApprovalSubmission.sql", provider == DatabaseProvider.SqlServer);
        Assert.AreEqual(1, SampleMigrationRecoverySupport.Run(provider, cs, "005_EnterpriseRequestApprovalSubmission.sql"));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM demo_enterprise_request_approval_submission"));
        Assert.AreEqual(instance, await connection.ExecuteScalarAsync<Guid>("SELECT WorkflowInstanceId FROM demo_enterprise_request_approval_submission WHERE TenantId = @TenantId AND RequestId = @RequestId", parameters));
        await Assert.ThrowsAsync<DbException>(async () => await connection.ExecuteAsync(insert, new { Id = Guid.CreateVersion7(), TenantId = Guid.CreateVersion7(),
            RequestId = Guid.CreateVersion7(), DefinitionId = parameters.DefinitionId, InstanceId = instance,
            parameters.ActorId, parameters.UnitId, parameters.Now }));
    }

}
