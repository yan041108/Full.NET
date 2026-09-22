using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dapper;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Api;

internal static class TenantQuotaMetricIdReconciliationAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        await VerifyForbiddenWithoutPermissionAsync(client, cancellationToken);
        await VerifyDryRunOnCleanDatabaseAsync(factory, client, cancellationToken);
        await VerifyRepairsNullMetricIdReservationAsync(factory, client, cancellationToken);
    }

    private static async Task VerifyForbiddenWithoutPermissionAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/tenancy/quota/reservations/reconcile-metric-ids")
        {
            Content = JsonContent.Create(new ReconcileTenantQuotaMetricIdsRequest(DryRun: true)),
        };
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task VerifyDryRunOnCleanDatabaseAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var token = await factory.CreateHostAccessTokenAsync(
            ["tenancy.tenant_quota.reconcile_metric_ids"],
            cancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/tenancy/quota/reservations/reconcile-metric-ids")
        {
            Content = JsonContent.Create(new ReconcileTenantQuotaMetricIdsRequest(DryRun: true)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ReconcileTenantQuotaMetricIdsResponse>(
            cancellationToken);
        Assert.IsNotNull(body);
        Assert.IsTrue(body!.DryRun);
        Assert.AreEqual(0, body.OutstandingCount);
        Assert.AreEqual(0, body.RepairedCount);
    }

    private static async Task VerifyRepairsNullMetricIdReservationAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var readToken = await factory.CreateHostAccessTokenAsync(
            [
                "tenancy.tenant_quota.read",
                "tenancy.host_tenants.read",
            ],
            cancellationToken);
        var reconcileToken = await factory.CreateHostAccessTokenAsync(
            ["tenancy.tenant_quota.reconcile_metric_ids"],
            cancellationToken);
        var tenantId = await ResolveAcmeTenantIdAsync(client, readToken, cancellationToken);
        var metricId = await ResolveSeatsMetricIdAsync(client, readToken, tenantId, cancellationToken);
        var reservationId = Guid.CreateVersion7();
        var operationId = $"reconcile-test-{reservationId:N}";
        await InsertNullMetricReservationAsync(
            factory,
            reservationId,
            tenantId,
            operationId,
            cancellationToken);

        using var dryRunRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/tenancy/quota/reservations/reconcile-metric-ids")
        {
            Content = JsonContent.Create(new ReconcileTenantQuotaMetricIdsRequest(DryRun: true)),
        };
        dryRunRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", reconcileToken);
        using var dryRunResponse = await client.SendAsync(dryRunRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, dryRunResponse.StatusCode);
        var dryRun = await dryRunResponse.Content.ReadFromJsonAsync<ReconcileTenantQuotaMetricIdsResponse>(
            cancellationToken);
        Assert.IsNotNull(dryRun);
        Assert.IsGreaterThanOrEqualTo(dryRun!.OutstandingCount, 1);
        Assert.AreEqual(dryRun.OutstandingCount, dryRun.RepairedCount);

        using var applyRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/tenancy/quota/reservations/reconcile-metric-ids")
        {
            Content = JsonContent.Create(new ReconcileTenantQuotaMetricIdsRequest(DryRun: false)),
        };
        applyRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", reconcileToken);
        using var applyResponse = await client.SendAsync(applyRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, applyResponse.StatusCode);
        var applied = await applyResponse.Content.ReadFromJsonAsync<ReconcileTenantQuotaMetricIdsResponse>(
            cancellationToken);
        Assert.IsNotNull(applied);
        Assert.IsFalse(applied!.DryRun);
        Assert.IsGreaterThanOrEqualTo(applied.RepairedCount, 1);

        var boundMetricId = await ReadReservationMetricIdAsync(
            factory,
            reservationId,
            cancellationToken);
        Assert.AreEqual(metricId, boundMetricId);
    }

    private static async Task<Guid> ResolveAcmeTenantIdAsync(
        HttpClient client,
        string token,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tenancy/tenants?page=1&pageSize=20");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<PagedResult<TenantSummary>>(cancellationToken);
        Assert.IsNotNull(page);
        var tenant = page!.Items.First(item => item.Identifier == "acme");
        return tenant.Id;
    }

    private static async Task<Guid> ResolveSeatsMetricIdAsync(
        HttpClient client,
        string token,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/tenancy/tenants/{tenantId:D}/quota/metrics");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var metrics = await response.Content.ReadFromJsonAsync<IReadOnlyList<TenantQuotaMetricResponse>>(
            cancellationToken);
        Assert.IsNotNull(metrics);
        var metric = metrics!.First(m =>
            m.MetricCode == TenantQuotaMetricCodes.IdentitySeats
            && m.PeriodKey == TenantQuotaDefaults.PeriodKey);
        return metric.Id;
    }

    private static async Task InsertNullMetricReservationAsync(
        FullNetApiFactory factory,
        Guid reservationId,
        Guid tenantId,
        string operationId,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddHours(1);
        if (factory.Provider == DatabaseProvider.SqlServer)
        {
            await using var connection = new SqlConnection(factory.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await connection.ExecuteAsync(
                """
                INSERT INTO dbo.fn_tenancy_quota_reservation
                    (Id, TenantId, MetricId, MetricCode, OperationId, Amount, Status, ExpiresAtUtc,
                     CreatedAtUtc, UpdatedAtUtc, Version)
                VALUES
                    (@Id, @TenantId, NULL, @MetricCode, @OperationId, 1, 'reserved', @ExpiresAtUtc,
                     @CreatedAtUtc, @UpdatedAtUtc, 1)
                """,
                new
                {
                    Id = reservationId,
                    TenantId = tenantId,
                    MetricCode = TenantQuotaMetricCodes.IdentitySeats,
                    OperationId = operationId,
                    ExpiresAtUtc = expires,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                });
            return;
        }

        var idHex = Convert.ToHexString(reservationId.ToByteArray(bigEndian: true));
        var tenantHex = Convert.ToHexString(tenantId.ToByteArray(bigEndian: true));
        await using var mysql = new MySqlConnection(factory.ConnectionString);
        await mysql.OpenAsync(cancellationToken);
        await mysql.ExecuteAsync(
            """
            INSERT INTO fn_tenancy_quota_reservation
                (Id, TenantId, MetricId, MetricCode, OperationId, Amount, Status, ExpiresAtUtc,
                 CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES
                (UNHEX(@IdHex), UNHEX(@TenantHex), NULL, @MetricCode, @OperationId, 1, 'reserved', @ExpiresAtUtc,
                 @CreatedAtUtc, @UpdatedAtUtc, 1)
            """,
            new
            {
                IdHex = idHex,
                TenantHex = tenantHex,
                MetricCode = TenantQuotaMetricCodes.IdentitySeats,
                OperationId = operationId,
                ExpiresAtUtc = expires.UtcDateTime,
                CreatedAtUtc = now.UtcDateTime,
                UpdatedAtUtc = now.UtcDateTime,
            });
    }

    private static async Task<Guid?> ReadReservationMetricIdAsync(
        FullNetApiFactory factory,
        Guid reservationId,
        CancellationToken cancellationToken)
    {
        if (factory.Provider == DatabaseProvider.SqlServer)
        {
            await using var connection = new SqlConnection(factory.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            return await connection.QuerySingleOrDefaultAsync<Guid?>(
                "SELECT MetricId FROM dbo.fn_tenancy_quota_reservation WHERE Id = @Id",
                new { Id = reservationId });
        }

        var idHex = Convert.ToHexString(reservationId.ToByteArray(bigEndian: true));
        await using var mysql = new MySqlConnection(factory.ConnectionString);
        await mysql.OpenAsync(cancellationToken);
        var bytes = await mysql.QuerySingleOrDefaultAsync<byte[]>(
            "SELECT MetricId FROM fn_tenancy_quota_reservation WHERE Id = UNHEX(@IdHex)",
            new { IdHex = idHex });
        return bytes is null ? null : new Guid(bytes, bigEndian: true);
    }
}
