extern alias workerhost;

using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Identity;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.EnterpriseRequest;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MySqlConnector;
using RealOutboxProcessor = workerhost::Full.NET.Host.Worker.OutboxProcessor;
using RealOutboxOptions = workerhost::Full.NET.Host.Worker.OutboxWorkerOptions;

namespace Full.NET.IntegrationTests.EnterpriseRequest;

public sealed partial class EnterpriseRequestAccessMatrixTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Live_approval_revocation_keeps_business_pending_until_authorized_retry(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection, includeEnterpriseRequest: true);
        await factory.InitializeAsync();
        using var owner = factory.CreateClientForHost("localhost");
        var ownerHost = await IntegrationTestAuthHelper.LoginAsHostUserAsync(owner, "admin");
        owner.DefaultRequestHeaders.Authorization = new("Bearer", ownerHost);
        var tenant = (await owner.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"))!.Single(t => t.Identifier == "acme");
        var ownerTenant = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(owner, ownerHost, tenant.Id);
        owner.DefaultRequestHeaders.Authorization = new("Bearer", ownerTenant);
        await EnterpriseRequestWorkflowBootstrap.PublishDemoApprovalDefinitionInTenantAsync(owner, ownerTenant);
        var unit = await EnterpriseRequestAssertions.CreateOrganizationUnitAsync(owner, ownerTenant, default);
        await IdentityOrganizationUnitProjectionTestHelper.BackfillTenantAsync(factory, tenant.Id);
        string[] permissions = [EnterpriseRequestPermissions.Read, EnterpriseRequestPermissions.Create, EnterpriseRequestDetailPermissions.Read,
            EnterpriseRequestWorkflowConstants.SubmitPermission, WorkflowPermissions.TodosRead, WorkflowPermissions.TodosApprove,
            "tenancy.tenants.read", "tenancy.tenants.switch"];
        var identity = await PrepareMemberAsync(factory, owner, permissions);
        await SendAsync(owner, HttpMethod.Post, "/api/v1/organization/user-units", new CreateOrganizationUserUnitRequest(identity.UserId, unit, true), HttpStatusCode.Created);
        using var administrator = factory.CreateClientForHost("localhost");
        administrator.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(administrator, "admin"));
        var assignments = (await administrator.GetFromJsonAsync<HostUserRolesResponse>($"/api/v1/identity/users/{identity.UserId:D}/roles"))!;
        var roleId = assignments.RoleIds.Single(); var role = (await ReadRoleAsync())!;
        await SendAsync(administrator, HttpMethod.Put, $"/api/v1/identity/roles/{roleId:D}/data-scope",
            new UpdateHostRoleDataScopeRequest(RoleDataScopeKinds.Custom, [unit], role.Version, tenant.Id), HttpStatusCode.OK);
        using var member = factory.CreateClientForHost("localhost"); await SignInAsync();
        Assert.IsFalse((await member.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("isSuperAdministrator").GetBoolean());
        var created = await SendAsync(member, HttpMethod.Post, Root + "/aggregate", Create(identity.UserId, "审批撤权业务"), HttpStatusCode.OK, unit);
        var requestId = created.GetProperty("master").GetProperty("id").GetGuid();
        var path = $"{Root}/{requestId:D}";
        var submitted = await SendAsync(member, HttpMethod.Post, path + "/submit-for-approval", null, HttpStatusCode.OK);
        var submittedVersion = submitted.GetProperty("version").GetString();
        await SendAsync(member, HttpMethod.Post, path + "/submit-for-approval", null, HttpStatusCode.OK);

        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection)
            : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        // 使用生产循环消费提交与终态事件；本场景不将同进程宿主描述成进程崩溃恢复。
        using var worker = await IntegrationWorkerHostFactory.BuildAsync(provider, connection,
            new Dictionary<string, string?>(), "Full.NET.IntegrationTests.EnterpriseRequest.Authorization.Worker", typeof(RealOutboxProcessor).Name,
            fileRootPath: factory.Services.GetRequiredService<IConfiguration>()["Files:Local:RootPath"]!,
            configureServices: services =>
            {
                services.AddSingleton(factory.Services.GetRequiredService<IDataProtectionProvider>());
                services.AddOptions<RealOutboxOptions>().Configure(options =>
                {
                    options.PollMilliseconds = 100; options.MaximumIdlePollMilliseconds = 1000;
                    options.BatchSize = 20; options.MaxConcurrency = 1; options.LeaseSeconds = 10; options.LeaseRenewalSeconds = 3;
                });
                services.AddHostedService<RealOutboxProcessor>();
            }, applicationModules: [new EnterpriseRequestModule()]);
        Assert.IsTrue(worker.Services.GetServices<IHostedService>().Any(service => service is RealOutboxProcessor));
        await worker.StartAsync();
        try
        {
            Guid todoId = Guid.Empty;
            await UntilAsync(async () => (todoId = await db.QuerySingleOrDefaultAsync<Guid>(
                "SELECT t.Id FROM fn_workflow_todo t INNER JOIN fn_workflow_instance i ON i.Id=t.InstanceId WHERE i.BusinessId=@id AND t.StatusKey='active'",
                new { id = requestId.ToString("D") })) != Guid.Empty);
            var detail = await SendAsync(member, HttpMethod.Get, $"/api/v1/workflow/todos/{todoId:D}/runtime", null, HttpStatusCode.OK);
            var instanceId = await db.ExecuteScalarAsync<Guid>("SELECT InstanceId FROM fn_workflow_todo WHERE Id=@todoId", new { todoId });
            var approval = new { expectedRevision = 1, expectedFormRevision = detail.GetProperty("submissionRevision").GetInt64(),
                fieldPatch = new { }, comment = "当前授权后办理", idempotencyKey = "enterprise-live-" + requestId.ToString("N") };
            await ReplacePermissionsAsync(permissions.Where(code => code != WorkflowPermissions.TodosApprove).ToArray());
            // 不刷新原会话，证明已打开待办和旧令牌不能保留被撤销的动作资格。
            var invalidSession = await SendAsync(member, HttpMethod.Post, $"/api/v1/workflow/todos/{todoId:D}/approve", approval, HttpStatusCode.Unauthorized);
            Assert.AreEqual("identity.session_not_active", invalidSession.GetProperty("code").GetString());
            await SignInAsync();
            await SendAsync(member, HttpMethod.Post, $"/api/v1/workflow/todos/{todoId:D}/approve", approval, HttpStatusCode.Forbidden);
            var pending = await SendAsync(member, HttpMethod.Get, path, null, HttpStatusCode.OK);
            Assert.AreEqual("Submitted", pending.GetProperty("status").GetString());
            Assert.AreEqual(submittedVersion, pending.GetProperty("version").GetString());
            Assert.AreEqual("active", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_todo WHERE Id=@todoId", new { todoId }));
            Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_domain_audit WHERE InstanceId=@instanceId AND OperationKey='todo.approve'", new { instanceId }));
            await ReplacePermissionsAsync(permissions); await SignInAsync();
            var approved = await SendAsync(member, HttpMethod.Post, $"/api/v1/workflow/todos/{todoId:D}/approve", approval, HttpStatusCode.OK);
            var replay = await SendAsync(member, HttpMethod.Post, $"/api/v1/workflow/todos/{todoId:D}/approve", approval, HttpStatusCode.OK);
            Assert.AreEqual(approved.GetRawText(), replay.GetRawText());
            await UntilAsync(async () => await db.ExecuteScalarAsync<string>("SELECT Status FROM demo_enterprise_request_enterprise_request WHERE Id=@requestId", new { requestId }) == "Approved");
            Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_instance WHERE BusinessId=@id", new { id = requestId.ToString("D") }));
            Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_domain_audit WHERE InstanceId=@instanceId AND OperationKey='todo.approve'", new { instanceId }));
        }
        finally { await worker.StopAsync(); }

        Task<HostRoleResponse?> ReadRoleAsync() => administrator.GetFromJsonAsync<HostRoleResponse>($"/api/v1/identity/roles/{roleId:D}");
        async Task ReplacePermissionsAsync(string[] codes)
        {
            var current = (await ReadRoleAsync())!;
            await SendAsync(administrator, HttpMethod.Put, $"/api/v1/identity/roles/{roleId:D}/permissions", new ReplaceHostRolePermissionsRequest(codes, current.Version), HttpStatusCode.OK);
        }
        async Task SignInAsync()
        {
            member.DefaultRequestHeaders.Authorization = null;
            var host = await IntegrationTestAuthHelper.LoginAsHostUserAsync(member, identity.Username);
            member.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(member, host, tenant.Id));
        }
        static async Task UntilAsync(Func<Task<bool>> condition)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (!await condition()) await Task.Delay(100, timeout.Token);
        }
    }
}
