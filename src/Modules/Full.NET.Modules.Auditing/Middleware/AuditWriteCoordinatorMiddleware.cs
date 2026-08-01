using Full.NET.Modules.Auditing.Features.WriteAuditBatch;
using Microsoft.AspNetCore.Http;

namespace Full.NET.Modules.Auditing.Middleware;

/// <summary>
/// 请求管道退出时：B1 Operation/Exception 入队并等待微批结果；忽略请求取消令牌。
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
            var snapshot = buffer.Snapshot();
            await coordinator.FlushImportantAsync(
                    snapshot.Operation,
                    snapshot.Exception,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
    }
}
