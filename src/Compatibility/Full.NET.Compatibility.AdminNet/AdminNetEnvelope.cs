namespace Full.NET.Compatibility.AdminNet;

/// <summary>
/// Admin.NET 统一响应信封，由 <see cref="AdminNetApiResultMapper"/> 把 <c>Result&lt;T&gt;</c> 映射而成。
/// </summary>
/// <remarks>
/// 仅用于兼容层线格式：<c>Success</c> 与 HTTP 状态码分离（状态码仍由标准映射决定），
/// <c>Code</c> 在启用 PreV1 兼容时回填历史错误码，<c>TraceId</c> 取当前 Activity 或请求标识。
/// </remarks>
/// <typeparam name="T">成功载荷类型；失败时 <c>Data</c> 为 <c>default</c>。</typeparam>
/// <param name="Success">业务是否成功，与 HTTP 状态码独立。</param>
/// <param name="Code">稳定错误码或 <c>success</c>；PreV1 模式下回填历史码。</param>
/// <param name="Message">本地化后的展示文本，失败时必填。</param>
/// <param name="Data">成功载荷；失败时为 <c>default</c>。</param>
/// <param name="TraceId">当前请求追踪标识，用于跨日志关联。</param>
public sealed record AdminNetEnvelope<T>(
    bool Success,
    string Code,
    string? Message,
    T? Data,
    string TraceId);
