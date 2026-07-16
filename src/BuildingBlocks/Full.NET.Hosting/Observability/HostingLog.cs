using Microsoft.Extensions.Logging;

namespace Full.NET.Hosting.Observability;

internal static partial class HostingLog
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Error,
        Message = "Unhandled exception for {RequestPath}")]
    public static partial void UnhandledException(
        ILogger logger,
        Exception exception,
        string requestPath);
}
