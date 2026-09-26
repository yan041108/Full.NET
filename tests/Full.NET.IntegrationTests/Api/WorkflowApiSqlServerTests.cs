using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Workflow;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class WorkflowApiSqlServerTests
{
    [TestMethod]
    public async Task Draft_and_publish_contracts_hold_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
        await WorkflowApiAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Start_creates_self_assigned_todo_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
        await WorkflowRuntimeApiAssertions.VerifyStartAsync(factory);
    }

    [TestMethod]
    public async Task Tenant_scope_approval_matrix_holds_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            settingsOverrides: new Dictionary<string, string?>
            {
                ["Identity:SessionLoginPolicy"] = "AllowMultiple",
            });
        await WorkflowRuntimeApiAssertions.VerifyTenantScopeAsync(factory);
    }

    [TestMethod]
    public async Task Enforced_feature_workflow_binding_gates_tenant_start_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            settingsOverrides: new Dictionary<string, string?>
            {
                ["Identity:SessionLoginPolicy"] = "AllowMultiple",
            });
        await WorkflowRuntimeApiAssertions.VerifyEnforcedTenantWorkflowStartRequiresFeatureBindingAsync(factory);
    }

    /// <summary>验证 SQL Server Worker 会扫描并暂停无法自动修复的工作流实例。</summary>
    [TestMethod]
    public async Task Recovery_worker_scans_and_suspends_unrecoverable_instance_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
        await WorkflowRuntimeApiAssertions.VerifyRecoveryWorkerAsync(factory);
    }

    /// <summary>验证 SQL Server 上三人 N-of-M 审批的票数进度、收敛和幂等回放。</summary>
    [TestMethod]
    public async Task Multi_approval_contract_holds_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
        await WorkflowRuntimeApiAssertions.VerifyMultiApprovalAsync(factory);
    }

    /// <summary>验证 SQL Server 上审批退回的执行链、并发幂等和事务副作用。</summary>
    [TestMethod]
    public async Task Todo_return_contract_holds_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
        await WorkflowRuntimeApiAssertions.VerifyReturnAsync(factory);
    }
}
