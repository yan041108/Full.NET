using Serilog;
using Serilog.Configuration;
using Serilog.Events;
using Serilog.Sinks.Elasticsearch;

namespace Full.NET.Hosting.Observability;

/// <summary>将可选 Elasticsearch Sink 挂接到现有 Serilog Audit 分支，复用双通道管道。</summary>
internal static class ElasticsearchSerilogSinkConfigurator
{
    /// <summary>在启用时向 Audit Sink 追加 Elasticsearch 写入器。</summary>
    /// <param name="sinkConfiguration">Serilog Audit 配置器。</param>
    /// <param name="options">Elasticsearch 日志选项。</param>
    public static void AppendIfEnabled(
        LoggerSinkConfiguration sinkConfiguration,
        ElasticsearchLoggingOptions options)
    {
        ArgumentNullException.ThrowIfNull(sinkConfiguration);
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled)
        {
            return;
        }

        var nodes = options.NodeUris
            .Where(uri => !string.IsNullOrWhiteSpace(uri))
            .Select(uri => new Uri(uri.Trim(), UriKind.Absolute))
            .ToArray();
        if (nodes.Length == 0)
        {
            return;
        }

        if (!ElasticsearchLoggingOptionsValidator.TryParseMinimumLevel(
                options.MinimumLevel,
                out var minimumLevel))
        {
            minimumLevel = LogEventLevel.Information;
        }

        var sinkOptions = new ElasticsearchSinkOptions(nodes)
        {
            IndexFormat = options.IndexFormat,
            AutoRegisterTemplate = true,
            AutoRegisterTemplateVersion = AutoRegisterTemplateVersion.ESv8,
            MinimumLogEventLevel = minimumLevel,
            ModifyConnectionSettings = connection =>
            {
                if (!string.IsNullOrWhiteSpace(options.ApiKey))
                {
                    connection = connection.ApiKeyAuthentication(
                        new Elasticsearch.Net.ApiKeyAuthenticationCredentials(options.ApiKey));
                }

                return connection;
            },
        };

        sinkConfiguration.Elasticsearch(sinkOptions);
    }
}
