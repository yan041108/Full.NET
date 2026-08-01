using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Auditing.Features.WriteAuditBatch;
using Full.NET.Modules.Auditing.Features.WriteOperationLogs;
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
                "auditing.rollback.probe"));
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
    }
}
