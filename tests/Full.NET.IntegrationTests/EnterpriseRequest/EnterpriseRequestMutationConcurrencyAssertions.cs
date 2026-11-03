using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Full.NET.IntegrationTests.EnterpriseRequest;

/// <summary>在真实数据库父单锁内暂停明细写入，观察提交阻塞后再恢复，验证检查与写入之间没有状态竞态。</summary>
internal static class EnterpriseRequestMutationConcurrencyAssertions
{
    internal static async Task VerifyAsync(DatabaseProvider provider, string connectionString)
    {
        var gate = new ParentLockGate();
        using var factory = new FullNetApiFactory(provider, connectionString, configureTestServices: services =>
        {
            var registration = services.Last(item => item.ServiceType == typeof(IQueryExecutor));
            services.Remove(registration);
            services.Add(ServiceDescriptor.Describe(typeof(IQueryExecutor), scope => new PausingQueryExecutor(
                (IQueryExecutor)(registration.ImplementationInstance ?? registration.ImplementationFactory?.Invoke(scope)
                    ?? ActivatorUtilities.CreateInstance(scope, registration.ImplementationType!)), gate), registration.Lifetime));
        }, includeEnterpriseRequest: true);
        await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("localhost");
        var token = await EnterpriseRequestAssertions.LoginAndEnterAcmeTenantAsync(client, default);
        await EnterpriseRequestWorkflowBootstrap.PublishDemoApprovalDefinitionInTenantAsync(client, token);
        var unit = await EnterpriseRequestAssertions.CreateOrganizationUnitAsync(client, token, default);
        var actor = await EnterpriseRequestAssertions.GetCurrentUserIdAsync(client, token, default);
        const string root = "/api/v1/enterprise_request/enterprise-requests";
        using var create = new HttpRequestMessage(HttpMethod.Post, root + "/aggregate")
        {
            Content = JsonContent.Create(new CreateEnterpriseRequestAggregateRequest(
                new($"REQ-{Guid.NewGuid():N}"[..24], "Concurrent detail", "Draft", 1m, actor), [new(1, "Before", 1m, 1m, 1m)]),
                options: new JsonSerializerOptions(JsonSerializerDefaults.Web)),
        };
        create.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        create.Headers.Add(OrganizationRequestHeaders.OrganizationUnitId, unit.ToString("D"));
        using var created = await client.SendAsync(create);
        var createdText = await created.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, created.StatusCode, createdText);
        using var parsed = JsonDocument.Parse(createdText);
        var master = parsed.RootElement.GetProperty("master");
        var detail = parsed.RootElement.GetProperty("details")[0];
        var id = master.GetProperty("id").GetGuid();
        var detailId = detail.GetProperty("id").GetGuid();
        var version = long.Parse(detail.GetProperty("version").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        await using var scope = factory.Services.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
        var resolver = scope.ServiceProvider.GetRequiredService<IActiveTenantContextResolver>();
        writer.SetTenant((await resolver.ResolveActiveByIdAsync(master.GetProperty("tenantId").GetGuid()))!);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var service = scope.ServiceProvider.GetRequiredService<EnterpriseRequestDetailManagementService>();
        gate.Armed = 1;
        var update = service.UpdateAsync(detailId, new(id, 1, "Before submit committed", 2m, 1m, 2m, version), actor, timeout.Token);
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), timeout.Token);
            using var submit = new HttpRequestMessage(HttpMethod.Post, $"{root}/{id}/submit-for-approval");
            submit.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var submission = client.SendAsync(submit, timeout.Token);
            bool observed;
            try { observed = await ObserveBlockedRequestAsync(provider, connectionString, submission, timeout.Token); }
            finally { gate.Release.TrySetResult(); }
            var updated = await update;
            Assert.IsTrue(updated.IsSuccess, updated.Error?.Code);
            using var submitted = await submission;
            Assert.AreEqual(HttpStatusCode.OK, submitted.StatusCode, await submitted.Content.ReadAsStringAsync(timeout.Token));
            Assert.IsTrue(observed, "明细事务持有父单锁期间必须观察到真实提交请求等待数据库锁。");
            var late = await service.UpdateAsync(detailId, new(id, 1, "After submit", 3m, 1m, 3m, updated.Value!.Version), actor, timeout.Token);
            Assert.AreEqual("enterprise_request.enterprise_requests.invalid_status", late.Error!.Code);
        }
        finally
        {
            gate.Release.TrySetResult();
            await update.WaitAsync(TimeSpan.FromSeconds(10));
            writer.Clear();
        }
    }

    private static async Task<bool> ObserveBlockedRequestAsync(DatabaseProvider provider, string connectionString,
        Task<HttpResponseMessage> submission, CancellationToken token)
    {
        // 诊断只使用测试夹具的管理连接；不为应用账号授予 performance_schema 读取权限。
        var diagnosticConnection = provider == DatabaseProvider.MySql
            ? new MySqlConnectionStringBuilder(await SharedDatabaseFixture.GetMySqlRootConnectionStringAsync())
            { Database = new MySqlConnectionStringBuilder(connectionString).Database }.ConnectionString : connectionString;
        await using DbConnection connection = provider == DatabaseProvider.MySql
            ? new MySqlConnection(diagnosticConnection) : new SqlConnection(diagnosticConnection);
        await connection.OpenAsync(token);
        var sql = provider == DatabaseProvider.MySql
            ? "SELECT COUNT(*) FROM performance_schema.data_lock_waits waits INNER JOIN performance_schema.data_locks locks ON locks.ENGINE_LOCK_ID=waits.REQUESTING_ENGINE_LOCK_ID AND locks.ENGINE=waits.ENGINE WHERE locks.OBJECT_SCHEMA=DATABASE() AND locks.OBJECT_NAME='demo_enterprise_request_enterprise_request'"
            : "SELECT COUNT_BIG(*) FROM sys.dm_exec_requests WHERE database_id=DB_ID() AND wait_type LIKE 'LCK[_]%' AND status='suspended'";
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline && !submission.IsCompleted)
        {
            if (await connection.ExecuteScalarAsync<long>(new CommandDefinition(sql, cancellationToken: token)) > 0) return true;
            await Task.Delay(40, token);
        }
        return false;
    }

    private sealed class ParentLockGate
    {
        internal int Armed;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class PausingQueryExecutor(IQueryExecutor inner, ParentLockGate gate) : IQueryExecutor
    {
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) =>
            inner.QueryAsync<T>(statement, parameters, cancellationToken);

        public async Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            var result = await inner.QuerySingleOrDefaultAsync<T>(statement, parameters, cancellationToken);
            if (statement.Name == "enterprise_request.draft_mutation.lock_parent" && Interlocked.Exchange(ref gate.Armed, 0) == 1)
            {
                gate.Entered.TrySetResult();
                await gate.Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
