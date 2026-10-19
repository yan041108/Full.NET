using Serilog.Core;
using Serilog.Events;
using Full.NET.Abstractions.Observability;

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

        var correlation = LogCorrelationContext.Capture();
        Add("TraceId", correlation.TraceId);
        Add("SpanId", correlation.SpanId);
        Add("ParentSpanId", correlation.ParentSpanId);
        Add("RequestId", correlation.RequestId);
        Add("UserId", correlation.UserId);
        Add("TenantId", correlation.TenantId);
        Add("SessionId", correlation.SessionId);
        Add("CenterSessionId", correlation.CenterSessionId);
        Add("ApplicationSessionId", correlation.ApplicationSessionId);
        Add("ClientIpFingerprint", correlation.ClientIpFingerprint);

        void Add(string name, object? value)
        {
            // 关联键由可信上下文独占；缺失时也要删除调用方同名属性，避免匿名或后台事件误关联。
            if (value is not null) logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty(name, value));
            else logEvent.RemovePropertyIfPresent(name);
        }

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
