using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Reporting;

/// <summary>正式双库分页使用同一状态谓词，并保持当前租户和定义边界及租户内可见历史。</summary>
[TestClass]
public sealed class ReportingExportStatusFilterApiTests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public async Task Status_filter_counts_pages_and_tenant_scope_match(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync(); using var client = factory.CreateClientForHost("localhost");
        var hostToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, "admin"); client.DefaultRequestHeaders.Authorization = new("Bearer", hostToken);
        var tenant = (await client.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"))!.Single(item => item.Identifier == "acme");
        client.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(client, hostToken, tenant.Id));
        await using var database = MyTasksApiTests.Open(provider, connection); await database.OpenAsync();
        var definition = Guid.CreateVersion7(); var otherDefinition = Guid.CreateVersion7(); var otherTenant = Guid.CreateVersion7();
        var otherUser = Guid.CreateVersion7(); var now = DateTime.UtcNow; var visibleIds = new HashSet<Guid>(); var foreignIds = new HashSet<Guid>();
        string[] states = ["queued", "processing", "succeeded", "failed", "canceled"];
        foreach (var (state, index) in states.Select((state, index) => (state, index)))
        {
            for (var row = 0; row < 4; row++)
            {
                var id = Guid.CreateVersion7(); if (row == 3) foreignIds.Add(id); else visibleIds.Add(id);
                await database.ExecuteAsync("""
                    INSERT INTO fn_reporting_export_task
                    (Id,TenantId,DefinitionId,VersionNumber,DefinitionKey,DefinitionName,FormatKey,ParametersJson,StatusKey,
                     RequestedByUserId,CreatedAtUtc,CompletedAtUtc,Version,LeaseId,LeaseExpiresAtUtc,ActorPermissionCodesJson,ActorSessionBindingJson)
                    VALUES (@Id,@TenantId,@DefinitionId,1,'status_filter','Status report','excel','[]',@StatusKey,
                     @UserId,@CreatedAtUtc,@CompletedAtUtc,1,@LeaseId,@LeaseExpiresAtUtc,'[]',NULL)
                    """, new { Id = id, TenantId = row == 3 ? otherTenant : tenant.Id, DefinitionId = row == 1 ? otherDefinition : definition,
                        StatusKey = state, UserId = otherUser, CreatedAtUtc = now.AddSeconds(-(index * 10 + row)),
                        CompletedAtUtc = state is "succeeded" or "failed" or "canceled" ? (DateTime?)now : null,
                        LeaseId = state == "processing" ? (Guid?)Guid.CreateVersion7() : null, LeaseExpiresAtUtc = state == "processing" ? (DateTime?)now.AddMinutes(5) : null });
            }
        }
        foreach (var state in states)
        {
            var first = await Read($"?page=1&pageSize=2&statusKey={state}"); var second = await Read($"?page=2&pageSize=2&statusKey={state}");
            Assert.AreEqual(3L, first.Total); Assert.AreEqual(3L, second.Total); Assert.AreEqual(2, first.Items.Count); Assert.AreEqual(1, second.Items.Count);
            var combined = first.Items.Concat(second.Items).ToArray(); Assert.AreEqual(3, combined.Select(item => item.Id).Distinct().Count());
            Assert.IsTrue(combined.All(item => item.StatusKey == state && visibleIds.Contains(item.Id) && !foreignIds.Contains(item.Id)));
            Assert.IsTrue(combined.All(item => item.RequestedByUserId == otherUser));
            var byDefinition = await Read($"?definitionId={definition:D}&statusKey={state}"); Assert.AreEqual(2L, byDefinition.Total);
            Assert.IsTrue(byDefinition.Items.All(item => item.DefinitionId == definition && item.StatusKey == state));
        }
        Assert.AreEqual(15L, (await Read("?pageSize=100")).Total); Assert.AreEqual(15L, (await Read("?statusKey=")).Total);
        Assert.AreEqual(3L, (await Read("?statusKey=%20succeeded%20")).Total);
        foreach (var value in new[] { "unknown", "SUCCEEDED", "failed%27%20OR%201=1" })
        {
            using var rejected = await client.GetAsync("/api/v1/reporting/export-tasks?statusKey=" + value); Assert.AreEqual(HttpStatusCode.BadRequest, rejected.StatusCode);
            using var problem = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync()); Assert.AreEqual(CommonErrorCodes.RequestInvalid, problem.RootElement.GetProperty("code").GetString());
        }
        using var anonymous = factory.CreateClientForHost("localhost"); using var unauthenticated = await anonymous.GetAsync("/api/v1/reporting/export-tasks?statusKey=failed");
        Assert.AreEqual(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        async Task<PagedResult<ReportingExportTaskResponse>> Read(string query)
        {
            using var response = await client.GetAsync("/api/v1/reporting/export-tasks" + query); response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<PagedResult<ReportingExportTaskResponse>>())!;
        }
    }
}
