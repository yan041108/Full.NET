using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Auditing.Features.WriteAuditBatch;
using Full.NET.Modules.Auditing.Features.WriteOperationLogs;
using Full.NET.Modules.Auditing.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Auditing;

/// <summary>
/// 验证 B1 微批：同批失败整批回滚，毒记录二分隔离后健康行可提交。
/// </summary>
internal static class AuditingBatchRollbackAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();

        var writer = scope.ServiceProvider.GetRequiredService<AuditWriteBatchWriter>();
        var queryExecutor = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();

        var healthyTrace = $"b1-ok-{Guid.NewGuid():N}";
        var poisonTrace = $"b1-poison-{Guid.NewGuid():N}";
        var contextJson = """
            {"schemaVersion":1,"clientIp":"2001:db8::1","requestCaptureState":"captured","requestSummary":{"fromUtc":"2026-09-27T00:00:00Z","toUtc":"2026-09-28T00:00:00Z","statusCode":200,"succeeded":true},"responseCaptureState":"captured","responseSummary":{"rowCount":12,"truncated":false,"includesSensitiveFields":true},"unapproved":"secret"}
            """;
        var healthy = AuditWriteEnvelope.ForOperation(
            new OperationLogWriteModel(
                "auditing.microbatch.healthy",
                "POST",
                "/api/v1/auditing/rollback-probe",
                200,
                1,
                true,
                null,
                null,
                healthyTrace,
                null,
                "auditing.rollback.probe")
            {
                Details = new AuditOperationDetails(
                    contextJson,
                    DateTimeOffset.UtcNow.AddHours(4)),
            });
        var poison = AuditWriteEnvelope.ForOperation(
            new OperationLogWriteModel(
                null!,
                "POST",
                "/api/v1/auditing/rollback-probe",
                500,
                1,
                false,
                null,
                null,
                poisonTrace,
                null,
                "auditing.rollback.probe"));

        await writer.WriteMicroBatchAsync([healthy, poison], cancellationToken);

        Assert.IsTrue((await healthy.Completion.Task).Succeeded);
        var poisonResult = await poison.Completion.Task;
        Assert.IsFalse(poisonResult.Succeeded);
        Assert.IsTrue(poisonResult.Poisoned);

        var healthyCount = await queryExecutor.QuerySingleOrDefaultAsync<long>(
            new SqlStatement(
                "test.auditing.count_b1_healthy_after_poison_split",
                """
                SELECT COUNT(*)
                FROM fn_auditing_operation_log
                WHERE TraceId = @TraceId
                """,
                SqlDataScope.Global),
            new { TraceId = healthyTrace },
            cancellationToken);
        var poisonCount = await queryExecutor.QuerySingleOrDefaultAsync<long>(
            new SqlStatement(
                "test.auditing.count_b1_poison_after_isolation",
                """
                SELECT COUNT(*)
                FROM fn_auditing_operation_log
                WHERE TraceId = @TraceId
                """,
                SqlDataScope.Global),
            new { TraceId = poisonTrace },
            cancellationToken);

        Assert.AreEqual(1L, healthyCount);
        Assert.AreEqual(0L, poisonCount);
        var persistedDetails = await queryExecutor.QuerySingleOrDefaultAsync<long>(
            new SqlStatement(
                "test.auditing.count_b1_atomic_detail_after_poison_split",
                """
                SELECT COUNT(*)
                FROM fn_auditing_operation_log
                WHERE TraceId = @TraceId
                  AND ContextJson = @ContextJson
                  AND DetailsExpiresAtUtc IS NOT NULL
                """,
                SqlDataScope.Global),
            new { TraceId = healthyTrace, ContextJson = contextJson },
            cancellationToken);
        Assert.AreEqual(1L, persistedDetails);

        var operationLogId = await queryExecutor.QuerySingleOrDefaultAsync<Guid>(
            new SqlStatement(
                "test.auditing.find_b1_detail_probe_id",
                "SELECT Id FROM fn_auditing_operation_log WHERE TraceId = @TraceId",
                SqlDataScope.Global),
            new { TraceId = healthyTrace },
            cancellationToken);
        Assert.AreNotEqual(Guid.Empty, operationLogId);

        using var client = factory.CreateClientForHost("localhost");
        var route = $"/api/v1/auditing/operation-logs/{operationLogId:D}/details";
        using (var unauthenticated = await client.GetAsync(route, cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        }

        using (var readOnlyRequest = new HttpRequestMessage(HttpMethod.Get, route))
        {
            readOnlyRequest.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                await factory.CreateHostAccessTokenAsync(
                    [OperationLogPermissions.Read], cancellationToken));
            using var forbidden = await client.SendAsync(readOnlyRequest, cancellationToken);
            Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        using (var detailsOnlyRequest = new HttpRequestMessage(HttpMethod.Get, route))
        {
            detailsOnlyRequest.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                await factory.CreateHostAccessTokenAsync(
                    [OperationLogPermissions.ReadDetails], cancellationToken));
            using var forbidden = await client.SendAsync(detailsOnlyRequest, cancellationToken);
            Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        var authorizedToken = await factory.CreateHostAccessTokenAsync(
            [OperationLogPermissions.Read, OperationLogPermissions.ReadDetails],
            cancellationToken);

        using (var detailRequest = new HttpRequestMessage(HttpMethod.Get, route))
        {
            detailRequest.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", authorizedToken);
            using var detailResponse = await client.SendAsync(detailRequest, cancellationToken);
            Assert.AreEqual(HttpStatusCode.OK, detailResponse.StatusCode);
            using var body = JsonDocument.Parse(
                await detailResponse.Content.ReadAsStringAsync(cancellationToken));
            var context = body.RootElement.GetProperty("context");
            Assert.AreEqual("2001:db8::1", context.GetProperty("clientIp").GetString());
            Assert.AreEqual(200, context.GetProperty("requestSummary")
                .GetProperty("statusCode").GetInt32());
            Assert.AreEqual(12, context.GetProperty("responseSummary")
                .GetProperty("rowCount").GetInt32());
            Assert.IsFalse(context.TryGetProperty("unapproved", out _));
        }

        await scope.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(
            new SqlStatement(
                "test.auditing.expire_b1_detail_probe",
                "UPDATE fn_auditing_operation_log SET DetailsExpiresAtUtc = @ExpiredAtUtc WHERE Id = @OperationLogId",
                SqlDataScope.Global),
            new { ExpiredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1), OperationLogId = operationLogId },
            cancellationToken);

        using var expiredRequest = new HttpRequestMessage(HttpMethod.Get, route);
        expiredRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", authorizedToken);
        using var expiredResponse = await client.SendAsync(expiredRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, expiredResponse.StatusCode);
    }
}
