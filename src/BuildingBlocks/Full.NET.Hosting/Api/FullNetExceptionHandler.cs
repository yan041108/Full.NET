using Full.NET.Hosting.Observability;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Full.NET.Hosting.Api;

/// <summary>
/// ASP.NET Core 全局异常处理器，接管管道未捕获异常并统一转换为 ProblemDetails 响应。
/// 处理流程：先通过结构化日志记录异常分类与 TraceId，再委托 <see cref="IApiResultMapper"/>
/// 输出符合宿主协议（标准或 Admin.NET 兼容信封）的 HTTP 响应体，始终返回 true 表示异常已吞。
/// </summary>
public sealed class FullNetExceptionHandler(
    /// <summary>
    /// 应用结果到 HTTP 响应的映射器；可根据宿主配置切换标准 ProblemDetails 或 Admin.NET 统一信封。
    /// </summary>
    IApiResultMapper mapper,
    ILogger<FullNetExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>
    /// 尝试处理管道中未捕获的异常；写入审计日志后统一映射为 ProblemDetails，永不向客户端暴露原始堆栈。
    /// </summary>
    /// <param name="httpContext">当前 HTTP 上下文。</param>
    /// <param name="exception">未捕获异常实例。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>始终返回 true，表示异常已由本处理器完成响应封装。</returns>
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        HostingLog.UnhandledException(logger, exception, httpContext.Request.Path);
        await mapper.MapException(exception, httpContext).ExecuteAsync(httpContext);
        return true;
    }
}
