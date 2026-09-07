namespace Full.NET.Modules.K3Cloud.Domain;

/// <summary>判断金蝶远程调用是否留下不可回滚的未知副作用。</summary>
internal static class UnknownExternalSideEffect
{
    /// <summary>Save/Submit 允许占用的本地租约，超时后才允许另一请求重试。</summary>
    public static readonly TimeSpan InvocationLease = TimeSpan.FromSeconds(45);

    /// <summary>HTTP 超时、取消和传输失败时，金蝶单据可能已经保存。</summary>
    /// <param name="exception">捕获的异常。</param>
    /// <returns>应按未知状态收敛而不是标记失败时返回 true。</returns>
    public static bool Matches(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or TimeoutException
        or OperationCanceledException;

    /// <summary>判断 pending 同步是否仍处于远程调用租约内。</summary>
    /// <param name="updatedAtUtc">最近一次意图或领取时间。</param>
    /// <param name="now">当前业务时钟。</param>
    /// <returns>仍应由原请求独占远程调用时返回 true。</returns>
    public static bool HasActiveLease(DateTimeOffset? updatedAtUtc, DateTimeOffset now) =>
        updatedAtUtc is DateTimeOffset updated && now - updated < InvocationLease;
}
