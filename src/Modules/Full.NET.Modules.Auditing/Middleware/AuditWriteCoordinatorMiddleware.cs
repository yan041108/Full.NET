using Full.NET.Modules.Auditing.Features.WriteAuditBatch;
using Microsoft.AspNetCore.Http;

namespace Full.NET.Modules.Auditing.Middleware;

/// <summary>
/// 全局异常映射完成后：更新 B1 Operation 最终 HTTP 状态，入队 Operation/Exception 并等待微批结果；忽略请求取消令牌。
/// </summary>
internal sealed class AuditWriteCoordinatorMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext httpContext,
        AuditWriteBuffer buffer,
        AuditMicroBatchCoordinator coordinator)
    {
        try
        {
            await next(httpContext).ConfigureAwait(false);
        }
        finally
        {
            // 客户端断开不能连带取消最终持久化尝试。
            buffer.CompleteOperation(httpContext.Response.StatusCode);
            var snapshot = buffer.Snapshot();
            try
            {
                await coordinator.FlushImportantAsync(
                        snapshot.Operation,
                        snapshot.Exception,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 外层协调器不得把 B1 故障变成业务失败，也不能覆盖原始异常。
                AuditMicroBatchTelemetry.RecordFailed("coordinator_unavailable");
            }
        }
    }
}
