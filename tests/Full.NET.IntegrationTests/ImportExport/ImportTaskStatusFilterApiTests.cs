using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.ImportExport;

/// <summary>九状态源页和总数同时过滤，保留 Schema 与可信租户边界及企业内可见历史。</summary>
[TestClass]
public sealed class ImportTaskStatusFilterApiTests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public async Task Status_counts_pages_schema_and_tenant_scope_match(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync(); using var client = factory.CreateClientForHost("localhost");
        var hostToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, "admin"); client.DefaultRequestHeaders.Authorization = new("Bearer", hostToken);
        var tenant = (await client.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"))!.Single(value => value.Identifier == "acme");
        client.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(client, hostToken, tenant.Id));
        await using var database = MyTasksApiTests.Open(provider, connection); await database.OpenAsync();
        var otherTenant = Guid.CreateVersion7(); var otherUser = Guid.CreateVersion7(); var now = DateTime.UtcNow; var visibleIds = new HashSet<Guid>(); var foreignIds = new HashSet<Guid>();
        const string schema = "retired.status_fixture";
        foreach (var state in ImportExportTaskStatusKeys.All)
        {
            for (var row = 0; row < 4; row++)
            {
                var id = Guid.CreateVersion7(); if (row == 3) foreignIds.Add(id); else visibleIds.Add(id);
                await database.ExecuteAsync("""
                    INSERT INTO fn_import_export_task
                    (Id,TenantId,SchemaKey,SchemaDisplayName,WorksheetKey,SourceFileId,SourceFileName,SourceExpiresAtUtc,StatusKey,TotalRows,ValidRowCount,InvalidRowCount,RequestedByUserId,CreatedAtUtc,Version)
                    VALUES (@Id,@Tenant,@Schema,'Status fixture','fixture',@Source,'fixture.xlsx',@Expires,@Status,0,0,0,@Owner,@Now,1)
                    """, new { Id = id, Tenant = row == 3 ? otherTenant : tenant.Id, Schema = row < 2 ? schema : "other.status_fixture", Source = Guid.CreateVersion7(), Expires = now.AddDays(7), Status = state, Owner = otherUser, Now = now.AddSeconds(row) });
            }
            var first = await Read($"?statusKey={state}&pageSize=2"); var second = await Read($"?statusKey={state}&pageSize=2&page=2");
            Assert.AreEqual(3L, first.Total); Assert.AreEqual(3L, second.Total); Assert.AreEqual(2, first.Items.Count); Assert.AreEqual(1, second.Items.Count);
            var rows = first.Items.Concat(second.Items).ToArray(); Assert.AreEqual(3, rows.Select(row => row.Id).Distinct().Count());
            Assert.IsTrue(rows.All(row => row.StatusKey == state && row.TenantId == tenant.Id && row.RequestedByUserId == otherUser && visibleIds.Contains(row.Id) && !foreignIds.Contains(row.Id)));
            var combined = await Read($"?statusKey={state}&schemaKey={schema}"); Assert.AreEqual(2L, combined.Total); Assert.IsTrue(combined.Items.All(row => row.SchemaKey == schema && row.StatusKey == state));
        }
        Assert.AreEqual(27L, (await Read("?pageSize=100")).Total); Assert.AreEqual(27L, (await Read("?statusKey=%20&schemaKey=%20")).Total);
        Assert.AreEqual(2L, (await Read($"?statusKey=%20execution_failed%20&schemaKey=%20{schema}%20")).Total);
        foreach (var query in new[] { "statusKey=unknown", "statusKey=EXECUTION_FAILED", "statusKey=queued%27%20OR%201=1", "schemaKey=" + new string('x', 129) })
        {
            using var rejected = await client.GetAsync("/api/v1/import-export/tasks?" + query); Assert.AreEqual(HttpStatusCode.BadRequest, rejected.StatusCode);
            using var problem = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync()); Assert.AreEqual(CommonErrorCodes.RequestInvalid, problem.RootElement.GetProperty("code").GetString());
        }
        using var anonymous = factory.CreateClientForHost("localhost"); using var denied = await anonymous.GetAsync("/api/v1/import-export/tasks?statusKey=execution_failed"); Assert.AreEqual(HttpStatusCode.Unauthorized, denied.StatusCode);
        async Task<PagedResult<ImportExportTaskResponse>> Read(string query)
        {
            using var response = await client.GetAsync("/api/v1/import-export/tasks" + query); response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<PagedResult<ImportExportTaskResponse>>())!;
        }
    }
}
