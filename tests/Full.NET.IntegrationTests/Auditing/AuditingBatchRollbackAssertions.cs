using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Auditing.Features.WriteAccessLogs;
using Full.NET.Modules.Auditing.Features.WriteAuditBatch;
using Full.NET.Modules.Auditing.Features.WriteOperationLogs;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Auditing;

/// <summary>
/// 使用第二条 INSERT 的约束失败验证请求级 Audit 批次不会留下已执行的第一条记录。
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

        var traceId = $"rollback-{Guid.NewGuid():N}";
        var buffer = scope.ServiceProvider.GetRequiredService<AuditWriteBuffer>();
        buffer.Capture(
            new AccessLogWriteModel(
                "POST",
                "/api/v1/auditing/rollback-probe",
                500,
                1,
                null,
                null,
                traceId,
                null,
                true));
        buffer.Capture(
            new OperationLogWriteModel(
                null!,
                "POST",
                "/api/v1/auditing/rollback-probe",
                500,
                1,
                false,
                null,
                null,
                traceId,
                null,
                "auditing.rollback.probe"));

        var succeeded = await scope.ServiceProvider
            .GetRequiredService<AuditWriteBatchWriter>()
            .TryWriteAsync(buffer, cancellationToken);

        Assert.IsFalse(succeeded);
        var persistedRows = await scope.ServiceProvider
            .GetRequiredService<IQueryExecutor>()
            .QuerySingleOrDefaultAsync<long>(
                new SqlStatement(
                    "test.auditing.count_rolled_back_request_batch",
                    """
                    SELECT
                        (SELECT COUNT(*)
                         FROM fn_auditing_access_log
                         WHERE TraceId = @TraceId)
                        +
                        (SELECT COUNT(*)
                         FROM fn_auditing_operation_log
                         WHERE TraceId = @TraceId)
                    """,
                    SqlDataScope.Global),
                new { TraceId = traceId },
                cancellationToken);
        Assert.AreEqual(0L, persistedRows);
    }
}
