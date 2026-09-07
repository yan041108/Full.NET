namespace Full.NET.Modules.Ocr.Domain;

/// <summary>判断 OCR 远程调用是否留下不可回滚的未知副作用。</summary>
internal static class UnknownExternalSideEffect
{
    /// <summary>HTTP 超时、取消和传输失败时，识别提供程序可能已经计费或落结果。</summary>
    /// <param name="exception">捕获的异常。</param>
    /// <returns>应按未知状态收敛而不是标记失败时返回 true。</returns>
    public static bool Matches(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or TimeoutException
        or OperationCanceledException;
}
