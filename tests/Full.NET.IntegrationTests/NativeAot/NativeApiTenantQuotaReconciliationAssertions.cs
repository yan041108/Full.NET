using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>在原生 API 上验证历史预留修复与非空配额回填，场景数据仅写入自有新建测试库。</summary>
internal static class NativeApiTenantQuotaReconciliationAssertions
{
    private const string ReservationPath = "/api/v1/tenancy/quota/reservations/reconcile-metric-ids";
    private const string BaselinePath = "/api/v1/tenancy/quota/usage-baseline";

    internal static async Task VerifyAsync(HttpClient client, string token, string logPath,
        DatabaseProvider provider, string connectionString, CancellationToken cancellationToken)
    {
        await PostAsync<ReconcileTenantQuotaMetricIdsResponse>(ReservationPath,
            new ReconcileTenantQuotaMetricIdsRequest(), null, HttpStatusCode.Unauthorized);
        await PostAsync<ReconcileTenantQuotaUsageBaselineResponse>(BaselinePath,
            new ReconcileTenantQuotaUsageBaselineRequest(), null, HttpStatusCode.Unauthorized);

        await using DbConnection database = provider == DatabaseProvider.SqlServer
            ? new SqlConnection(connectionString)
            : new MySqlConnection(MySqlConnectionStringPolicy.Create(connectionString,
                MySqlGuidStorageMode.Binary16, allowUserVariables: false));
        var username = $"native-quota-{Guid.NewGuid():N}";
        var unprivilegedUser = await PostAsync<HostUserResponse>("/api/v1/identity/users",
            new CreateHostUserRequest(username, "原生配额无权限用户", NativeApiE2EAssertions.AdminPassword),
            token, HttpStatusCode.Created);
        // 仅初始化本样例新账号；必须让后续请求到达权限策略，而非被首次改密中间件提前拒绝。
        Assert.AreEqual(1, await database.ExecuteAsync(new CommandDefinition("""
            UPDATE fn_identity_user SET MustChangePassword = 0
            WHERE Id = @Id AND TenantId IS NULL AND MustChangePassword = 1
            """, new { unprivilegedUser.Id }, cancellationToken: cancellationToken)));
        using (var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
               { Content = JsonContent.Create(new LoginRequest(username, NativeApiE2EAssertions.AdminPassword)) })
        {
            login.Headers.Add("Origin", "http://localhost");
            using var response = await client.SendAsync(login, cancellationToken);
            await NativeApiE2EAssertions.AssertStatusAsync(response, HttpStatusCode.OK,
                "Login unprivileged quota user", cancellationToken, logPath);
            var userToken = (await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken))!.AccessToken;
            await PostAsync<ReconcileTenantQuotaMetricIdsResponse>(ReservationPath,
                new ReconcileTenantQuotaMetricIdsRequest(), userToken, HttpStatusCode.Forbidden);
            await PostAsync<ReconcileTenantQuotaUsageBaselineResponse>(BaselinePath,
                new ReconcileTenantQuotaUsageBaselineRequest(), userToken, HttpStatusCode.Forbidden);
        }

        var tenants = await GetAsync<PagedResult<TenantSummary>>("/api/v1/tenancy/tenants?page=1&pageSize=100");
        var tenant = tenants.Items.Single(item => item.Identifier == "local");
        var metricPath = $"/api/v1/tenancy/tenants/{tenant.Id:D}/quota/metrics";
        var metric = (await GetAsync<TenantQuotaMetricResponse[]>(metricPath))
            .Single(item => item.MetricCode == TenantQuotaMetricCodes.IdentitySeats
                && item.PeriodKey == TenantQuotaDefaults.PeriodKey);
        var reservationId = Guid.CreateVersion7();
        var skippedId = Guid.CreateVersion7();
        object now = provider == DatabaseProvider.MySql ? (object)DateTime.UtcNow : DateTimeOffset.UtcNow;
        foreach (var id in new[] { reservationId, skippedId })
        {
            await database.ExecuteAsync(new CommandDefinition("""
                INSERT INTO fn_tenancy_quota_reservation
                    (Id, TenantId, MetricId, MetricCode, OperationId, Amount, Status, ExpiresAtUtc,
                     CreatedAtUtc, UpdatedAtUtc, Version)
                VALUES (@Id, @TenantId, NULL, @Code, @Operation, 1, @Status, @Now, @Now, @Now, 7)
                """, new { Id = id, TenantId = tenant.Id,
                    Code = id == reservationId ? TenantQuotaMetricCodes.IdentitySeats : "native.missing",
                    Operation = $"native-reconcile-{id:N}", Status = TenantQuotaReservationStatuses.Released, Now = now },
                cancellationToken: cancellationToken));
        }
        var preview = await PostAsync<ReconcileTenantQuotaMetricIdsResponse>(ReservationPath,
            new ReconcileTenantQuotaMetricIdsRequest(), token);
        Assert.IsTrue(preview.DryRun);
        Assert.AreEqual(2, preview.OutstandingCount);
        Assert.AreEqual(1, preview.RepairedCount);
        Assert.AreEqual(1, preview.SkippedCount);
        await AssertReservationAsync(reservationId, null, 7);
        await AssertReservationAsync(skippedId, null, 7);
        var applied = await PostAsync<ReconcileTenantQuotaMetricIdsResponse>(ReservationPath,
            new ReconcileTenantQuotaMetricIdsRequest(false), token);
        Assert.IsFalse(applied.DryRun);
        Assert.AreEqual(2, applied.OutstandingCount);
        Assert.AreEqual(1, applied.RepairedCount);
        Assert.AreEqual(1, applied.SkippedCount);
        await AssertReservationAsync(reservationId, metric.Id, 8);
        await AssertReservationAsync(skippedId, null, 7);
        var repeated = await PostAsync<ReconcileTenantQuotaMetricIdsResponse>(ReservationPath,
            new ReconcileTenantQuotaMetricIdsRequest(false), token);
        Assert.AreEqual(1, repeated.OutstandingCount);
        Assert.AreEqual(0, repeated.RepairedCount);
        Assert.AreEqual(1, repeated.SkippedCount);
        await AssertReservationAsync(reservationId, metric.Id, 8);

        // 先删除本样例的历史终态预留，再模拟升级前缺失的指标；不破坏其它测试记录。
        Assert.AreEqual(2, await database.ExecuteAsync(new CommandDefinition(
            "DELETE FROM fn_tenancy_quota_reservation WHERE Id IN (@Id, @SkippedId)",
            new { Id = reservationId, SkippedId = skippedId }, cancellationToken: cancellationToken)));
        Assert.AreEqual(1, await database.ExecuteAsync(new CommandDefinition(
            "DELETE FROM fn_tenancy_quota_metric WHERE Id = @Id AND TenantId = @TenantId",
            new { metric.Id, TenantId = tenant.Id }, cancellationToken: cancellationToken)));
        var expectedUsage = await database.ExecuteScalarAsync<long>(new CommandDefinition("""
            SELECT COUNT(1) FROM fn_identity_tenant_member WHERE TenantId = @TenantId AND Status = @Status
            """, new { TenantId = tenant.Id, Status = TenantMemberStatuses.Active }, cancellationToken: cancellationToken));
        Assert.IsTrue(expectedUsage > 0, "非空活动成员才可证明权威计数端口真实到达。");
        var baselinePreview = await PostAsync<ReconcileTenantQuotaUsageBaselineResponse>(BaselinePath,
            new ReconcileTenantQuotaUsageBaselineRequest(), token);
        Assert.IsTrue(baselinePreview.DryRun);
        Assert.Contains(tenant.Id, baselinePreview.TenantIds);
        Assert.AreEqual(0, baselinePreview.AppliedCount);
        Assert.IsFalse((await GetAsync<TenantQuotaMetricResponse[]>(metricPath)).Any(item => item.Id == metric.Id
            || (item.MetricCode == metric.MetricCode && item.PeriodKey == metric.PeriodKey)));
        var baseline = await PostAsync<ReconcileTenantQuotaUsageBaselineResponse>(BaselinePath,
            new ReconcileTenantQuotaUsageBaselineRequest(false), token);
        Assert.IsFalse(baseline.DryRun);
        Assert.Contains(tenant.Id, baseline.TenantIds);
        Assert.IsTrue(baseline.AppliedCount >= 1);
        var backfilled = (await GetAsync<TenantQuotaMetricResponse[]>(metricPath)).Single(item =>
            item.MetricCode == metric.MetricCode && item.PeriodKey == metric.PeriodKey);
        Assert.AreNotEqual(metric.Id, backfilled.Id);
        Assert.AreEqual(tenant.Id, backfilled.TenantId);
        Assert.AreEqual(TenantQuotaDefaults.IdentitySeatsLimit, backfilled.LimitValue);
        Assert.AreEqual(expectedUsage, backfilled.UsedValue);
        Assert.AreEqual(0L, backfilled.ReservedValue);
        var baselineRepeat = await PostAsync<ReconcileTenantQuotaUsageBaselineResponse>(BaselinePath,
            new ReconcileTenantQuotaUsageBaselineRequest(false), token);
        Assert.AreEqual(0, baselineRepeat.CandidateCount);
        Assert.AreEqual(0, baselineRepeat.AppliedCount);
        Assert.AreEqual(backfilled, (await GetAsync<TenantQuotaMetricResponse[]>(metricPath)).Single(item => item.Id == backfilled.Id));

        async Task AssertReservationAsync(Guid id, Guid? expectedMetric, int version)
        {
            var row = await database.QuerySingleAsync<(Guid? MetricId, int Version)>(new CommandDefinition(
                "SELECT MetricId, Version FROM fn_tenancy_quota_reservation WHERE Id = @Id",
                new { Id = id }, cancellationToken: cancellationToken));
            Assert.AreEqual(expectedMetric, row.MetricId);
            Assert.AreEqual(version, row.Version);
        }
        async Task<T> GetAsync<T>(string path)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request, cancellationToken);
            await NativeApiE2EAssertions.AssertStatusAsync(response, HttpStatusCode.OK, path, cancellationToken, logPath);
            return (await response.Content.ReadFromJsonAsync<T>(cancellationToken))!;
        }
        async Task<T> PostAsync<T>(string path, object body, string? accessToken,
            HttpStatusCode expected = HttpStatusCode.OK)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
            if (accessToken is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await client.SendAsync(request, cancellationToken);
            await NativeApiE2EAssertions.AssertStatusAsync(response, expected, path, cancellationToken, logPath);
            if (expected == HttpStatusCode.Forbidden)
            {
                using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                Assert.AreEqual(CommonErrorCodes.PermissionDenied, problem.RootElement.GetProperty("code").GetString());
            }
            return expected is HttpStatusCode.OK or HttpStatusCode.Created
                ? (await response.Content.ReadFromJsonAsync<T>(cancellationToken))! : default!;
        }
    }

    internal static async Task VerifyTenantDeniedAsync(HttpClient client, string tenantToken,
        string logPath, CancellationToken cancellationToken)
    {
        foreach (var path in new[] { ReservationPath, BaselinePath })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
                { Content = JsonContent.Create(new ReconcileTenantQuotaMetricIdsRequest()) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tenantToken);
            using var response = await client.SendAsync(request, cancellationToken);
            await NativeApiE2EAssertions.AssertStatusAsync(response, HttpStatusCode.Forbidden,
                "Reject tenant quota reconciliation", cancellationToken, logPath);
        }
    }
}
