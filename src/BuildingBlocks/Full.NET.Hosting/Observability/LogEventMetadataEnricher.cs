using Serilog.Core;
using Serilog.Events;

namespace Full.NET.Hosting.Observability;

/// <summary>
/// 在双通道分流前补齐普通日志的受控分类与单次记录 ID。
/// </summary>
internal sealed class LogEventMetadataEnricher : ILogEventEnricher
{
    private readonly string _instance;

    public LogEventMetadataEnricher(string instance)
    {
        _instance = instance;
    }

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(propertyFactory);

        List<KeyValuePair<string, string>>? oversizedScalars = null;
        foreach (var property in logEvent.Properties)
        {
            if (property.Value is not ScalarValue { Value: string value }
                || value.Length <= 2048)
            {
                continue;
            }

            var length = 2048;
            if (char.IsHighSurrogate(value[length - 1]))
            {
                length--;
            }

            oversizedScalars ??= [];
            oversizedScalars.Add(new KeyValuePair<string, string>(
                property.Key,
                value[..length]));
        }

        // Serilog 的解构字符串上限不覆盖普通标量；在分流前替换其引用，防止队列留住超长原值。
        if (oversizedScalars is not null)
        {
            foreach (var property in oversizedScalars)
            {
                logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty(
                    property.Key,
                    property.Value));
            }
        }

        if (!logEvent.Properties.ContainsKey("log.class"))
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
                "log.class",
                LogClassification.Diagnostic));
        }

        // ID 由日志管道生成，不接受调用方同名属性冒充持久去重键。
        logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty(
            "LogEventId",
            Guid.CreateVersion7().ToString("D")));
        // Instance 由启动时生成的资源元数据固定，不能由调用方伪造关联到其他进程。
        logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty(
            "Instance",
            _instance));
    }
}
