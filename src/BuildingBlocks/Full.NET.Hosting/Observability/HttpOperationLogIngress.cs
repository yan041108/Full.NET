using Serilog.Events;

namespace Full.NET.Hosting.Observability;

/// <summary>中间件专用的类型化入口；普通 ILogger 事件不能调用此入口。</summary>
public sealed class HttpOperationLogIngress
{
    private Func<HttpOperationLogRecord, LogEventLevel, bool>? _emit;

    internal void Attach(Func<HttpOperationLogRecord, LogEventLevel, bool> emit) =>
        Volatile.Write(ref _emit, emit);

    internal void Detach(Func<HttpOperationLogRecord, LogEventLevel, bool> emit) =>
        Interlocked.CompareExchange(ref _emit, null, emit);

    // null=管道未挂接；true=内存队列接受；false=有界队列拒绝。
    internal bool? Emit(HttpOperationLogRecord record, LogEventLevel level)
    {
        var emit = Volatile.Read(ref _emit);
        if (emit is null)
        {
            return null;
        }

        return emit(record, level);
    }
}
