using System.Text.Json;
using Full.NET.Hosting.Observability;
using Serilog.Events;
using Serilog.Parsing;

namespace Full.NET.UnitTests.Hosting;

/// <summary>仅供既有断言读取生产管道输出的 JSON；不参与生产序列化或后台对象保留。</summary>
internal static class LogSnapshotTestReader
{
    public static LogEvent Read(LogEnvelope envelope)
    {
        using var document = JsonDocument.Parse(envelope.Utf8Json);
        var root = document.RootElement;
        var level = root.TryGetProperty("@l", out var item)
            ? Enum.Parse<LogEventLevel>(item.GetString()!) : LogEventLevel.Information;
        return new LogEvent(root.GetProperty("@t").GetDateTimeOffset(), level,
            root.TryGetProperty("@x", out var error) ? new Exception(error.GetString()) : null,
            new MessageTemplateParser().Parse(root.GetProperty("@mt").GetString()!),
            root.EnumerateObject().Where(p => !p.Name.StartsWith('@'))
                .Select(p => new LogEventProperty(p.Name, ReadValue(p.Value))));
    }

    private static LogEventPropertyValue ReadValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => new ScalarValue(null),
        JsonValueKind.True => new ScalarValue(true),
        JsonValueKind.False => new ScalarValue(false),
        JsonValueKind.Number => value.TryGetInt32(out var number)
            ? new ScalarValue(number) : new ScalarValue(value.GetDouble()),
        JsonValueKind.String => new ScalarValue(value.GetString()),
        JsonValueKind.Array => new SequenceValue(value.EnumerateArray().Select(ReadValue)),
        JsonValueKind.Object => new DictionaryValue(value.EnumerateObject()
            .Select(p => new KeyValuePair<ScalarValue, LogEventPropertyValue>(new(p.Name), ReadValue(p.Value)))),
        _ => throw new InvalidOperationException("Unsupported snapshot value."),
    };
}
