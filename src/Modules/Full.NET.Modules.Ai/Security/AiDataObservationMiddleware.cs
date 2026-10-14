using Full.NET.AI.Abstractions.Models;
using Microsoft.AspNetCore.Http;

namespace Full.NET.Modules.Ai.Security;

/// <summary>AI 请求统一继承宿主诊断策略；业务授权、审计和结果不由此范围决定。</summary>
internal sealed class AiDataObservationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AiDataProcessingPolicy policy)
    {
        if (!context.Request.Path.StartsWithSegments("/api/v1/ai", StringComparison.OrdinalIgnoreCase))
        {
            await next(context).ConfigureAwait(false);
            return;
        }
        using var scope = AiDataObservationScope.Enter(policy);
        await next(context).ConfigureAwait(false);
    }
}
