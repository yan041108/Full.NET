using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Abstractions.Auditing;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Auditing;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Tenancy;

/// <summary>
/// 验证 Host 禁用租户路径的 B0 域内审计与业务 UPDATE 共享同一事务：
/// 成功时两者同提交，审计写入失败时两者同回滚，且全程不经过 Outbox。
/// </summary>
[TestClass]
public sealed class TenancyDomainAuditTransactionAssertions
{
    [TestMethod]
    public async Task SqlServer_disable_commits_domain_audit_together_with_the_business_update()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await VerifySuccessfulCommitAsync(factory);
    }

    [TestMethod]
    public async Task MySql_disable_commits_domain_audit_together_with_the_business_update()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await VerifySuccessfulCommitAsync(factory);
    }

    [TestMethod]
    public async Task SqlServer_disable_rolls_back_the_business_update_when_domain_audit_write_fails()
    {
        // 事务共享由 Provider 无关的 Dapper 环境事务实现（DapperCommandTransaction），
        // 因此回滚场景只需在一个 Provider 上用可控失败注入验证；两个 Provider 的
        // 同提交路径已分别由上面两个用例覆盖。
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            configureTestServices: services =>
            {
                services.RemoveAll<ITransactionalDomainAuditWriter<TenancyDomainAuditWrite>>();
                services.AddScoped<
                    ITransactionalDomainAuditWriter<TenancyDomainAuditWrite>,
                    ThrowingTenancyDomainAuditWriter>();
            });
        await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("localhost");
        var adminToken = await LoginAsHostAdminAsync(client);
        var created = await CreateTenantAsync(client, adminToken, "rollback");

        using var disableRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/tenancy/tenants/{created.Id:D}/disable",
            adminToken,
            new { });
        using var disableResponse = await client.SendAsync(disableRequest);

        Assert.AreEqual(
            HttpStatusCode.InternalServerError,
            disableResponse.StatusCode,
            "审计写入器抛出的异常必须冒泡给全局异常处理，而不是被吞掉后返回成功。");

        var stillActive = await GetTenantByIdAsync(client, adminToken, created.Id);
        Assert.IsTrue(
            stillActive.IsActive,
            "审计写入失败必须回滚同一事务内的业务 UPDATE，禁用不能生效。");
        Assert.AreEqual(
            created.Version,
            stillActive.Version,
            "回滚后乐观并发版本号不得被业务 UPDATE 提前推进。");
        Assert.AreEqual(
            0L,
            await CountDomainAuditRowsAsync(factory, created.Id),
            "审计写入失败时不得残留半条已提交的审计记录。");
    }

    private static async Task VerifySuccessfulCommitAsync(FullNetApiFactory factory)
    {
        await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("localhost");
        var adminToken = await LoginAsHostAdminAsync(client);
        var created = await CreateTenantAsync(client, adminToken, "audit-commit");
        var auditCountBeforeDisable = await CountDomainAuditRowsAsync(
            factory,
            created.Id);

        using var disableRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/tenancy/tenants/{created.Id:D}/disable",
            adminToken,
            new { });
        using var disableResponse = await client.SendAsync(disableRequest);

        Assert.AreEqual(HttpStatusCode.OK, disableResponse.StatusCode);
        var disabled = await disableResponse.Content
            .ReadFromJsonAsync<TenantSummary>();
        Assert.IsNotNull(disabled);
        Assert.IsFalse(disabled.IsActive);
        Assert.AreEqual(
            auditCountBeforeDisable + 1,
            await CountDomainAuditRowsAsync(factory, created.Id),
            "禁用成功必须与 B0 域内审计写入在同一次提交内一起落库。");
    }

    private static async Task<TenantSummary> CreateTenantAsync(
        HttpClient client,
        string adminToken,
        string identifierPrefix)
    {
        var identifier = $"{identifierPrefix}-{Guid.NewGuid():N}"[..20];
        var domain = $"{identifier}.localhost";
        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/tenancy/tenants",
            adminToken,
            new ProvisionTenantRequest(identifier, "待禁用租户", domain));
        using var createResponse = await client.SendAsync(createRequest);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<TenantSummary>();
        Assert.IsNotNull(created);
        return created;
    }

    private static async Task<TenantSummary> GetTenantByIdAsync(
        HttpClient client,
        string adminToken,
        Guid tenantId)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/tenancy/tenants/{tenantId:D}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var response = await client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var summary = await response.Content.ReadFromJsonAsync<TenantSummary>();
        Assert.IsNotNull(summary);
        return summary;
    }

    private static async Task<string> LoginAsHostAdminAsync(HttpClient client)
    {
        using var loginRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(
                new LoginRequest("admin", FullNetApiFactory.TestPassword)),
        };
        loginRequest.Headers.Add("Origin", "http://localhost");
        using var loginResponse = await client.SendAsync(loginRequest);
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
        var token = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.IsNotNull(token);
        return token.AccessToken;
    }

    private static async Task<long> CountDomainAuditRowsAsync(
        FullNetApiFactory factory,
        Guid entityId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var currentTenant = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        currentTenant.SetHost();
        try
        {
            return await scope.ServiceProvider
                .GetRequiredService<IQueryExecutor>()
                .QuerySingleOrDefaultAsync<long>(
                    new SqlStatement(
                        "test.tenancy.count_domain_audit_rows_by_entity_id",
                        """
                        SELECT COUNT(1)
                        FROM fn_tenancy_domain_audit
                        WHERE EntityId = @EntityId
                        """,
                        SqlDataScope.HostOnly),
                    new { EntityId = entityId });
        }
        finally
        {
            currentTenant.Clear();
        }
    }

    private static HttpRequestMessage CreateBearerJsonRequest<TRequest>(
        HttpMethod method,
        string path,
        string accessToken,
        TRequest body)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }
}

/// <summary>
/// 只用于验证回滚语义的测试替身：模拟审计写入在业务 UPDATE 之后失败，
/// 断言该失败会级联回滚同一事务内已经执行的业务写入。
/// </summary>
internal sealed class ThrowingTenancyDomainAuditWriter
    : ITransactionalDomainAuditWriter<TenancyDomainAuditWrite>
{
    public Task WriteAsync(
        TenancyDomainAuditWrite auditWrite,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(
            "模拟 B0 域内审计写入失败，用于验证与业务写入同回滚。");
}
