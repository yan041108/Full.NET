using Full.NET.Abstractions.Results;
using Microsoft.AspNetCore.Http;

namespace Full.NET.Hosting.Api;

/// <summary>
/// 应用层 <see cref="Result{T}"/> 与 HTTP 响应体之间的映射契约。
/// 宿主可根据需要切换标准映射（<see cref="StandardApiResultMapper"/>，输出 RFC ProblemDetails）
/// 或 Admin.NET 兼容信封映射（由 Compatibility 层提供，输出 code+msg+data 结构）。
/// </summary>
public interface IApiResultMapper
{
    /// <summary>
    /// 将 <see cref="Result{T}"/> 映射为 <see cref="IResult"/>；成功直接返回值，失败映射为对应 HTTP 状态码。
    /// </summary>
    /// <typeparam name="T">成功承载的数据类型。</typeparam>
    /// <param name="result">应用层返回的结构化结果。</param>
    /// <param name="httpContext">当前请求上下文，用于写入 TraceId、Locale 等响应头。</param>
    IResult Map<T>(Result<T> result, HttpContext httpContext);

    /// <summary>
    /// 将未捕获异常映射为标准失败 <see cref="IResult"/>；默认映射到 500 + Unexpected 错误码。
    /// </summary>
    /// <param name="exception">管道中未处理的异常实例。</param>
    /// <param name="httpContext">当前请求上下文。</param>
    IResult MapException(Exception exception, HttpContext httpContext);
}
