using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.Organization.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.EnterpriseRequest;

internal static partial class EnterpriseRequestAssertions
{
    internal static void ForceParentDeleteConflict(IServiceCollection services)
    {
        var factory = services.Last(item => item.ServiceType == typeof(ICommandExecutor)).ImplementationFactory!;
        services.AddScoped<ICommandExecutor>(provider => new ParentDeleteConflictExecutor((ICommandExecutor)factory(provider)));
    }

    public static async Task VerifyOrdinaryStateWritesAndCascadeRollbackAsync(FullNetApiFactory factory)
    {
        await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("localhost");
        var token = await LoginAndEnterAcmeTenantAsync(client, default);
        var unit = await CreateOrganizationUnitAsync(client, token, default);
        var body = new CreateEnterpriseRequestRequest($"STATE-{Guid.NewGuid():N}"[..16], "State boundary", "Draft", 1m, Guid.NewGuid());
        foreach (var status in new[] { "Submitted", "Approved", "Rejected", "Cancelled", "draft", "" })
        {
            using var denied = await Send(HttpMethod.Post, BasePath, body with { Status = status });
            await AssertDenied(denied, EnterpriseRequestWorkflowErrorCodes.InvalidStatus);
        }
        using var createdResponse = await Send(HttpMethod.Post, BasePath, body);
        Assert.AreEqual(HttpStatusCode.Created, createdResponse.StatusCode, await createdResponse.Content.ReadAsStringAsync());
        var created = await createdResponse.Content.ReadFromJsonAsync<EnterpriseRequestResponse>();
        Assert.IsNotNull(created);
        var path = $"{BasePath}/{created.Id:D}";
        var edit = new UpdateEnterpriseRequestRequest(body.RequestNumber, "Edited draft", "Draft", 1m, body.ApplicantUserId, created.Version);
        foreach (var status in new[] { "Submitted", "Approved", "Rejected", "Cancelled" })
        {
            using var denied = await Send(HttpMethod.Put, path, edit with { Status = status });
            await AssertDenied(denied, EnterpriseRequestWorkflowErrorCodes.InvalidStatus);
            Assert.AreEqual(created, await Read());
        }

        var lineId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
            tenant.SetTenant(new TenantContext(created.TenantId, "acme", "Acme"));
            try
            {
                await scope.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(new SqlStatement(
                    "enterprise_request.test_insert_line",
                    """
                    INSERT INTO demo_enterprise_request_enterprise_request_line
                    (Id, TenantId, RequestId, LineNumber, ItemDescription, Quantity, UnitPrice, LineAmount,
                     Version, CreatedAtUtc, CreatedById, IsDeleted)
                    VALUES (@Id, @TenantId, @RequestId, 1, 'Rollback probe', 1, 1, 1, 1, @CreatedAtUtc, @CreatedById, 0)
                    """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId),
                    new { Id = lineId, RequestId = created.Id, CreatedAtUtc = DateTimeOffset.UtcNow, CreatedById = created.CreatedById });
            }
            finally { tenant.Clear(); }
        }
        // 领域门读取及请求版本均合法，在父行 SQL 才注入 CAS 失败，确保真实执行了明细删除。
        using (var stale = await Send(HttpMethod.Post, $"{path}/delete", new DeleteEnterpriseRequestRequest(created.Version)))
            await AssertDenied(stale, EnterpriseRequestErrorCodes.VersionConflict);
        Assert.AreEqual(created, await Read());
        Assert.AreEqual(1L, await LineCount(), "父行 CAS 冲突必须回滚已经发生的明细删除。");

        using var edited = await Send(HttpMethod.Put, path, edit);
        Assert.AreEqual(HttpStatusCode.OK, edited.StatusCode, await edited.Content.ReadAsStringAsync());
        var draft = await edited.Content.ReadFromJsonAsync<EnterpriseRequestResponse>();
        Assert.IsNotNull(draft);
        await EnterpriseRequestWorkflowBootstrap.PublishDemoApprovalDefinitionInTenantAsync(client, token);
        using var submittedResponse = await Send(HttpMethod.Post, $"{path}/submit-for-approval", null);
        Assert.AreEqual(HttpStatusCode.OK, submittedResponse.StatusCode, await submittedResponse.Content.ReadAsStringAsync());
        var submitted = await submittedResponse.Content.ReadFromJsonAsync<EnterpriseRequestResponse>();
        Assert.IsNotNull(submitted);
        foreach (var status in new[] { "Draft", "Submitted", "Approved" })
        {
            using var denied = await Send(HttpMethod.Put, path, edit with { Status = status, Version = submitted.Version });
            await AssertDenied(denied, EnterpriseRequestWorkflowErrorCodes.InvalidStatus);
        }
        using (var deniedDelete = await Send(HttpMethod.Post, $"{path}/delete", new DeleteEnterpriseRequestRequest(submitted.Version)))
            await AssertDenied(deniedDelete, EnterpriseRequestWorkflowErrorCodes.InvalidStatus);
        Assert.AreEqual(submitted, await Read());
        Assert.AreEqual(1L, await LineCount());
        using (var deniedLines = await Send(HttpMethod.Put, $"{path}/lines", new { version = submitted.Version.ToString(), items = Array.Empty<object>() }))
            await AssertDenied(deniedLines, EnterpriseRequestWorkflowErrorCodes.InvalidStatus);

        async Task<HttpResponseMessage> Send(HttpMethod method, string url, object? payload)
        {
            using var request = new HttpRequestMessage(method, url);
            if (payload is not null) request.Content = JsonContent.Create(payload, payload.GetType());
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add(OrganizationRequestHeaders.OrganizationUnitId, unit.ToString("D"));
            return await client.SendAsync(request);
        }

        async Task<EnterpriseRequestResponse?> Read()
        {
            using var response = await Send(HttpMethod.Get, path, null);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            return await response.Content.ReadFromJsonAsync<EnterpriseRequestResponse>();
        }

        async Task<long> LineCount()
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
            tenant.SetTenant(new TenantContext(created.TenantId, "acme", "Acme"));
            try
            {
                return await scope.ServiceProvider.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<long>(new SqlStatement(
                    "enterprise_request.test_count_lines", "SELECT COUNT(*) FROM demo_enterprise_request_enterprise_request_line WHERE RequestId = @RequestId AND TenantId = @TenantId",
                    SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId), new { RequestId = created.Id });
            }
            finally { tenant.Clear(); }
        }
    }

    private static async Task AssertDenied(HttpResponseMessage response, string expectedCode)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode, text);
        using var json = JsonDocument.Parse(text);
        Assert.AreEqual(expectedCode, json.RootElement.GetProperty("code").GetString());
    }

    private sealed class ParentDeleteConflictExecutor(ICommandExecutor inner) : ICommandExecutor
    {
        public Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            if (statement == EnterpriseRequestSql.DeleteStatement)
                statement = statement with { Text = statement.Text.Replace("AND Version = @Version", "AND Version = @Version AND 1 = 0", StringComparison.Ordinal) };
            return inner.ExecuteAsync(statement, parameters, cancellationToken);
        }
    }
}
