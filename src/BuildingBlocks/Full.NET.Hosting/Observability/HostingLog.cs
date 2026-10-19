using Microsoft.Extensions.Logging;

namespace Full.NET.Hosting.Observability;

/// <summary>Hosting 层源生成日志事件；避免在异常路径手工拼接消息模板。</summary>
internal static partial class HostingLog
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Error,
        Message = "Unhandled {ExceptionType} for {RequestPath}")]
    public static partial void UnhandledException(
        ILogger logger,
        string exceptionType,
        string requestPath);
}
