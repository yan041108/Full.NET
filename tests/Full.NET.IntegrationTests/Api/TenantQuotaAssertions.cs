using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Abstractions.Results;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Api;

internal static class TenantQuotaAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        var token = await factory.CreateHostAccessTokenAsync(
            [
                "tenancy.tenant_quota.read",
                "tenancy.tenant_quota.manage",
                "tenancy.tenant_quota.reserve",
                "tenancy.host_tenants.read",
            ],
            cancellationToken);
        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/tenancy/tenants?page=1&pageSize=20");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var page = await listResponse.Content.ReadFromJsonAsync<PagedResult<TenantSummary>>(
            cancellationToken);
        Assert.IsNotNull(page);
        var tenant = page!.Items.First(item => item.Identifier == "acme");

        using var metricsRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/tenancy/tenants/{tenant.Id:D}/quota/metrics");
        metricsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var metricsResponse = await client.SendAsync(metricsRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, metricsResponse.StatusCode);
        var metrics = await metricsResponse.Content.ReadFromJsonAsync<IReadOnlyList<TenantQuotaMetricResponse>>(
            cancellationToken);
        Assert.IsNotNull(metrics);
        var seeded = metrics!.FirstOrDefault(metric =>
            metric.MetricCode == TenantQuotaMetricCodes.IdentitySeats
            && metric.PeriodKey == TenantQuotaDefaults.PeriodKey);
        Assert.IsNotNull(seeded);
        Assert.AreEqual(TenantQuotaDefaults.IdentitySeatsLimit, seeded!.LimitValue);

        using var upsertRequest = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/v1/tenancy/tenants/{tenant.Id:D}/quota/metrics")
        {
            Content = JsonContent.Create(new UpsertTenantQuotaMetricRequest(
                TenantQuotaMetricCodes.IdentitySeats,
                TenantQuotaDefaults.PeriodKey,
                200)),
        };
        upsertRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var upsertResponse = await client.SendAsync(upsertRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, upsertResponse.StatusCode);
        var metric = await upsertResponse.Content.ReadFromJsonAsync<TenantQuotaMetricResponse>(
            cancellationToken);
        Assert.IsNotNull(metric);
        Assert.AreEqual(200, metric!.LimitValue);

        using var limitOneRequest = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/v1/tenancy/tenants/{tenant.Id:D}/quota/metrics")
        {
            Content = JsonContent.Create(new UpsertTenantQuotaMetricRequest(
                TenantQuotaMetricCodes.IdentitySeats,
                TenantQuotaDefaults.PeriodKey,
                1)),
        };
        limitOneRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var limitOneResponse = await client.SendAsync(limitOneRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, limitOneResponse.StatusCode);

        var operationId = Guid.CreateVersion7().ToString("D");
        using var reserveOneRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/tenancy/tenants/{tenant.Id:D}/quota/reserve")
        {
            Content = JsonContent.Create(new ReserveTenantQuotaRequest(
                TenantQuotaMetricCodes.IdentitySeats,
                operationId,
                1)),
        };
        reserveOneRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var reserveOneResponse = await client.SendAsync(reserveOneRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, reserveOneResponse.StatusCode);

        using var reserveTwoRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/tenancy/tenants/{tenant.Id:D}/quota/reserve")
        {
            Content = JsonContent.Create(new ReserveTenantQuotaRequest(
                TenantQuotaMetricCodes.IdentitySeats,
                Guid.CreateVersion7().ToString("D"),
                1)),
        };
        reserveTwoRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var reserveTwoResponse = await client.SendAsync(reserveTwoRequest, cancellationToken);
        Assert.AreNotEqual(HttpStatusCode.OK, reserveTwoResponse.StatusCode);

        // 模拟响应丢失后的再次提交：终态重放不能再次增加用量或扣减预留。
        await AssertReserveAsync(operationId, 1, HttpStatusCode.OK);
        await AssertReserveAsync(operationId, 2, HttpStatusCode.Conflict);
        var released = await CompleteAsync("release", operationId);
        var releasedAgain = await CompleteAsync("release", operationId);
        Assert.AreEqual(released, releasedAgain);
        Assert.AreEqual(0, released.ReservedValue);
        await AssertReserveAsync(operationId, 1, HttpStatusCode.Conflict);

        var nextOperation = Guid.CreateVersion7().ToString("D");
        await AssertReserveAsync(nextOperation, 1, HttpStatusCode.OK);
        var confirmed = await CompleteAsync("confirm", nextOperation);
        await AssertReserveAsync(nextOperation, 1, HttpStatusCode.OK);
        var confirmedAgain = await CompleteAsync("confirm", nextOperation);
        Assert.AreEqual(confirmed, confirmedAgain);
        Assert.AreEqual(1, confirmed.UsedValue);
        Assert.AreEqual(0, confirmed.ReservedValue);
        using var inverseRequest = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v1/tenancy/tenants/{tenant.Id:D}/quota/release")
        {
            Content = JsonContent.Create(new ReleaseTenantQuotaRequest(nextOperation)),
        };
        inverseRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var inverseResponse = await client.SendAsync(inverseRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, inverseResponse.StatusCode);

        // 原周期预留后新增 default 配额，完成操作仍应返回原始月份记录。
        const string bindingCode = "test.binding";
        var month = DateTimeOffset.UtcNow.ToString("yyyy-MM");
        var monthly = await UpsertBindingMetricAsync(month);
        var boundOperation = Guid.CreateVersion7().ToString("D");
        await AssertReserveAsync(boundOperation, 1, HttpStatusCode.OK, bindingCode);
        await UpsertBindingMetricAsync(TenantQuotaDefaults.PeriodKey);
        var boundCompletion = await CompleteAsync("confirm", boundOperation);
        Assert.AreEqual(monthly.Id, boundCompletion.Id);
        Assert.AreEqual(month, boundCompletion.PeriodKey);
        Assert.AreEqual(1, boundCompletion.UsedValue);
        Assert.AreEqual(0, boundCompletion.ReservedValue);
        Assert.AreEqual(boundCompletion, await CompleteAsync("confirm", boundOperation));

        async Task<TenantQuotaMetricResponse> UpsertBindingMetricAsync(string period)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put,
                $"/api/v1/tenancy/tenants/{tenant.Id:D}/quota/metrics")
            {
                Content = JsonContent.Create(new UpsertTenantQuotaMetricRequest(bindingCode, period, 10)),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request, cancellationToken);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            var value = await response.Content.ReadFromJsonAsync<TenantQuotaMetricResponse>(cancellationToken);
            Assert.IsNotNull(value);
            return value;
        }

        async Task AssertReserveAsync(string id, long amount, HttpStatusCode expected,
            string metricCode = TenantQuotaMetricCodes.IdentitySeats)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"/api/v1/tenancy/tenants/{tenant.Id:D}/quota/reserve")
            {
                Content = JsonContent.Create(new ReserveTenantQuotaRequest(metricCode, id, amount)),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request, cancellationToken);
            Assert.AreEqual(expected, response.StatusCode);
        }

        async Task<TenantQuotaMetricResponse> CompleteAsync(string action, string id)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"/api/v1/tenancy/tenants/{tenant.Id:D}/quota/{action}")
            {
                Content = action == "confirm"
                    ? JsonContent.Create(new ConfirmTenantQuotaRequest(id))
                    : JsonContent.Create(new ReleaseTenantQuotaRequest(id)),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request, cancellationToken);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            var value = await response.Content.ReadFromJsonAsync<TenantQuotaMetricResponse>(cancellationToken);
            Assert.IsNotNull(value);
            return value;
        }
    }
}