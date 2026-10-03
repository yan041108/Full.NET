using Microsoft.Extensions.Logging;

namespace Full.NET.Hosting.Observability;

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
