using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Ai;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Caching;

/// <summary>后台租户准入实验必须先合法创建运行；模型读取沿用可信作用域，拒绝跨租户及禁用模型后不预留预算。</summary>
[TestClass]
[DoNotParallelize]
public sealed class CacheAgentRuntimeModelScopeTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Runtime_model_admission_supports_own_and_shared_models_without_host_catalog_bypass(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection,
            settingsOverrides: AiAgentRuntimeTestSettings.ForIntegrationTests());
        await factory.InitializeAsync();
        await CacheBackgroundAuthorityFixture.EnsureAnotherActiveTenantAsync(factory);
        using var host = factory.CreateClientForHost("localhost");
        var token = await CacheBackgroundAuthorityFixture.LoginAsync(host, "admin");
        host.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var tenants = await host.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available");
        Assert.IsNotNull(tenants);
        var tenant = tenants.Single(item => item.Identifier == "acme");
        var foreign = tenants.First(item => item.Id != tenant.Id);
        var shared = await CreateModelAsync(null, true);
        var own = await CreateModelAsync(tenant.Id, true);
        var other = await CreateModelAsync(foreign.Id, true);
        var disabled = await CreateModelAsync(tenant.Id, false);
        await AiAgentRunApiAssertions.SeedFreshWorkerHeartbeatAsync(factory);
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
        await RequireCreatedAsync(host, shared);
        await RequireRejectedAsync(host, own, HttpStatusCode.NotFound, AiErrorCodes.ModelConfigNotFound);

        using var client = factory.CreateClientForHost("localhost");
        var tenantToken = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(client, token, tenant.Id);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenantToken);
        await RequireCreatedAsync(client, own);
        await RequireCreatedAsync(client, shared);
        await RequireRejectedAsync(client, other, HttpStatusCode.NotFound, AiErrorCodes.ModelConfigNotFound);
        await RequireRejectedAsync(client, Guid.CreateVersion7(), HttpStatusCode.NotFound, AiErrorCodes.ModelConfigNotFound);
        await RequireRejectedAsync(client, disabled, HttpStatusCode.UnprocessableEntity, AiErrorCodes.ModelConfigUnavailable);

        async Task<Guid> CreateModelAsync(Guid? owner, bool enabled)
        {
            using var response = await host.PostAsJsonAsync("/api/v1/ai/model-configs",
                new CreateAiModelConfigRequest(owner, "Runtime Scope", "ollama", "https://provider.test", "model", null, null, false, enabled));
            Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
            var model = await response.Content.ReadFromJsonAsync<AiModelConfigResponse>();
            Assert.IsNotNull(model); Assert.AreEqual(owner, model.TenantId); return model.Id;
        }

        async Task RequireCreatedAsync(HttpClient actor, Guid model)
        {
            using var response = await actor.PostAsJsonAsync("/api/v1/ai/agent/runs", Request(model));
            Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode, await response.Content.ReadAsStringAsync());
            var run = await response.Content.ReadFromJsonAsync<CreateAiAgentRunResponse>();
            Assert.IsNotNull(run); Assert.AreNotEqual(Guid.Empty, run.RunId);
        }

        async Task RequireRejectedAsync(HttpClient actor, Guid model, HttpStatusCode status, string code)
        {
            var beforeRuns = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_agent_run");
            var beforeBudgets = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget");
            using var response = await actor.PostAsJsonAsync("/api/v1/ai/agent/runs", Request(model));
            await AiAgentRunApiAssertions.AssertProblemAsync(response, status, code);
            Assert.AreEqual(beforeRuns, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_agent_run"));
            Assert.AreEqual(beforeBudgets, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget"));
        }

        static CreateAiAgentRunRequest Request(Guid model) => new(Guid.CreateVersion7(), "fullnet-single-text-v1", model, "scope probe", 100, 100);
    }
}
