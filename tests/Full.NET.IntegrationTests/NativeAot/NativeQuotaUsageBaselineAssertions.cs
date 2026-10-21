using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>原生 HTTP 必须物化缺失指标的 NULL 标识，并使用非空租户游标继续读取；播种只在 JIT 侧执行。</summary>
internal static class NativeQuotaUsageBaselineAssertions
{
    internal static async Task VerifyAsync(DatabaseProvider provider, string connectionString, HttpClient client,
        string accessToken, CancellationToken cancellationToken)
    {
        var identifier = "native-quota-" + Guid.CreateVersion7().ToString("N");
        using (var create = Authorized(HttpMethod.Post, "/api/v1/tenancy/tenants", accessToken,
                   new ProvisionTenantRequest(identifier, "Native quota cursor", identifier + ".localhost")))
        using (var response = await client.SendAsync(create, cancellationToken))
            Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));

        await using DbConnection connection = provider == DatabaseProvider.MySql
            ? new MySqlConnection(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, false))
            : new SqlConnection(connectionString);
        var tenantId = await connection.QuerySingleAsync<Guid>(new CommandDefinition(
            "SELECT Id FROM fn_tenancy_tenant WHERE Identifier='local'", cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition("""
            DELETE FROM fn_tenancy_quota_metric WHERE TenantId=@TenantId AND MetricCode=@MetricCode AND PeriodKey=@PeriodKey
            """, new { TenantId = tenantId, MetricCode = TenantQuotaMetricCodes.FilesStorageBytes, TenantQuotaDefaults.PeriodKey },
            cancellationToken: cancellationToken));

        var candidateTenants = new HashSet<Guid>();
        var seenCursors = new HashSet<Guid>();
        Guid? after = null;
        var completed = false;
        var pages = 0;
        for (; pages < 10; pages++)
        {
            var page = await PostAsync(new ReconcileTenantQuotaUsageBaselineRequest(true, TenantQuotaMetricCodes.FilesStorageBytes)
                { PageSize = 1, AfterTenantId = after });
            Assert.IsTrue(page.DryRun);
            Assert.AreEqual(0, page.AppliedCount);
            Assert.IsLessThanOrEqualTo(1, page.CandidateCount);
            foreach (var candidate in page.TenantIds) Assert.IsTrue(candidateTenants.Add(candidate));
            if (page.IsComplete)
            {
                Assert.IsNull(page.NextAfterTenantId);
                completed = true;
                pages++;
                break;
            }
            Assert.IsNotNull(page.NextAfterTenantId);
            Assert.AreNotEqual(Guid.Empty, page.NextAfterTenantId.Value);
            Assert.IsTrue(seenCursors.Add(page.NextAfterTenantId.Value));
            after = page.NextAfterTenantId;
        }
        Assert.IsTrue(completed);
        Assert.IsTrue(pages >= 2, "必须实际执行包含非空后续游标的读取。");
        Assert.Contains(tenantId, candidateTenants);
        var applied = await PostAsync(new ReconcileTenantQuotaUsageBaselineRequest(false, TenantQuotaMetricCodes.FilesStorageBytes));
        Assert.IsTrue(applied.IsComplete);
        Assert.IsTrue(applied.AppliedCount >= 1);
        var restored = await connection.QuerySingleAsync<Guid>(new CommandDefinition("""
            SELECT Id FROM fn_tenancy_quota_metric WHERE TenantId=@TenantId AND MetricCode=@MetricCode AND PeriodKey=@PeriodKey
            """, new { TenantId = tenantId, MetricCode = TenantQuotaMetricCodes.FilesStorageBytes, TenantQuotaDefaults.PeriodKey },
            cancellationToken: cancellationToken));
        Assert.AreNotEqual(Guid.Empty, restored);
        var unchanged = await PostAsync(new ReconcileTenantQuotaUsageBaselineRequest(true, TenantQuotaMetricCodes.FilesStorageBytes));
        Assert.AreEqual(0, unchanged.CandidateCount);
        using var invalid = Authorized(HttpMethod.Post, "/api/v1/tenancy/quota/usage-baseline", accessToken,
            new ReconcileTenantQuotaUsageBaselineRequest { AfterTenantId = Guid.Empty });
        using var rejected = await client.SendAsync(invalid, cancellationToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, rejected.StatusCode);

        async Task<ReconcileTenantQuotaUsageBaselineResponse> PostAsync(ReconcileTenantQuotaUsageBaselineRequest body)
        {
            using var request = Authorized(HttpMethod.Post, "/api/v1/tenancy/quota/usage-baseline", accessToken, body);
            using var response = await client.SendAsync(request, cancellationToken);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
            var result = await response.Content.ReadFromJsonAsync<ReconcileTenantQuotaUsageBaselineResponse>(cancellationToken);
            Assert.IsNotNull(result);
            return result;
        }
    }

    private static HttpRequestMessage Authorized<T>(HttpMethod method, string path, string token, T body)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }
}
