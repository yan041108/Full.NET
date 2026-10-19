using System.Diagnostics;

namespace Full.NET.Hosting.Observability;

/// <summary>只在后台消费/退出边界使用的限频诊断，不回写 ILogger、不输出异常正文或载荷。</summary>
internal static class LoggingInternalDiagnostics
{
    private static long _nextReport;

    internal static void Report(string stage, Exception exception)
    {
        var now = Stopwatch.GetTimestamp();
        var next = Interlocked.Read(ref _nextReport);
        if (now < next || Interlocked.CompareExchange(ref _nextReport, now + Stopwatch.Frequency * 30L, next) != next) return;
        try { Console.Error.WriteLine($"Full.NET logging internal failure: {stage}; type={exception.GetType().Name}."); }
        catch (IOException) { /* 诊断输出失败不能替代原故障。 */ }
        catch (ObjectDisposedException) { /* stderr 已关闭时不递归报告。 */ }
    }
}
