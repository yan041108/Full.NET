using System.Diagnostics;

namespace Full.NET.Abstractions.Observability;

/// <summary>向日志生产端提供可信关联上下文；异步队列只能传递 Capture 返回的值，不能传递解析函数。</summary>
public static class LogCorrelationContext
{
    private static readonly AsyncLocal<Scope?> CurrentScope = new();

    /// <summary>建立服务端拥有的解析作用域；在请求结束时清除所有继承执行上下文中的引用。</summary>
    /// <param name="capture">仅从已验证上下文读取的同步函数，不得执行 I/O。</param>
    /// <returns>按嵌套逆序释放的作用域。</returns>
    public static IDisposable BeginScope(Func<LogCorrelationSnapshot> capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        var scope = new Scope(CurrentScope.Value, capture);
        CurrentScope.Value = scope;
        return scope;
    }

    /// <summary>立即冻结当前元数据；没有请求时仍可记录后台 Activity，绝不伪造身份。</summary>
    /// <returns>不持有请求或作用域服务引用的不可变快照。</returns>
    public static LogCorrelationSnapshot Capture()
    {
        var result = CurrentScope.Value?.Capture?.Invoke() ?? new LogCorrelationSnapshot();
        var activity = Activity.Current;
        return result with
        {
            TraceId = activity?.IdFormat == ActivityIdFormat.W3C ? activity.TraceId.ToHexString() : null,
            SpanId = activity?.IdFormat == ActivityIdFormat.W3C ? activity.SpanId.ToHexString() : null,
            ParentSpanId = activity?.IdFormat == ActivityIdFormat.W3C && activity.ParentSpanId != default
                ? activity.ParentSpanId.ToHexString() : null,
        };
    }

    private sealed class Scope(Scope? parent, Func<LogCorrelationSnapshot> capture) : IDisposable
    {
        public Func<LogCorrelationSnapshot>? Capture = capture;

        public void Dispose()
        {
            // 共享容器清空委托，使逃逸到后台的 ExecutionContext 不再持有 HttpContext。
            Capture = null;
            if (ReferenceEquals(CurrentScope.Value, this)) CurrentScope.Value = parent;
        }
    }
}
