using Full.NET.Modules.Auditing.Features.WriteAuditBatch;
using Microsoft.AspNetCore.Http;

namespace Full.NET.Modules.Auditing.Middleware;

/// <summary>
/// 位于三个 Audit 捕获 Middleware 外层，在请求管道退出前统一同步提交固定容量快照。
/// </summary>
internal sealed class AuditWriteCoordinatorMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext httpContext,
        AuditWriteBuffer buffer,
        AuditWriteBatchWriter writer)
    {
        try
        {
            await next(httpContext).ConfigureAwait(false);
        }
        finally
        {
            // 客户端断开不能连带取消安全审计的最后一次数据库提交尝试。
            await writer.TryWriteAsync(buffer, CancellationToken.None).ConfigureAwait(false);
        }
    }
}
