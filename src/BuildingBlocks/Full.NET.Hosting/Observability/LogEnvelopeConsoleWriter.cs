using System.Text;

namespace Full.NET.Hosting.Observability;

/// <summary>
/// 把已格式化的 UTF-8 快照输出到 Console，避免后台再走一次 Serilog 格式化。
/// </summary>
internal static class LogEnvelopeConsoleWriter
{
    private static readonly object Gate = new();

    public static void Emit(LogEnvelope envelope)
    {
        var line = Encoding.UTF8.GetString(envelope.Utf8Json.Span);
        lock (Gate)
        {
            Console.Out.Write(line);
        }
    }
}
