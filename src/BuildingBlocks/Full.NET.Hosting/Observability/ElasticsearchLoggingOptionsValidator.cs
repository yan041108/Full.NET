using Microsoft.Extensions.Options;

namespace Full.NET.Hosting.Observability;

/// <summary>启动期校验 Elasticsearch 日志 Sink 配置，防止启用后缺少节点或索引格式。</summary>
internal sealed class ElasticsearchLoggingOptionsValidator : IValidateOptions<ElasticsearchLoggingOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ElasticsearchLoggingOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var errors = new List<string>();
        if (options.NodeUris.Count == 0
            || options.NodeUris.All(string.IsNullOrWhiteSpace))
        {
            errors.Add("NodeUris must contain at least one URI when Elasticsearch logging is enabled.");
        }

        foreach (var nodeUri in options.NodeUris)
        {
            if (string.IsNullOrWhiteSpace(nodeUri))
            {
                continue;
            }

            if (!Uri.TryCreate(nodeUri.Trim(), UriKind.Absolute, out var parsed)
                || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                errors.Add($"Node URI '{nodeUri}' must be an absolute http or https URI.");
            }
            else if (!string.IsNullOrEmpty(parsed.UserInfo))
            {
                errors.Add("Node URIs must not embed credentials; use ApiKey instead.");
            }
        }

        if (string.IsNullOrWhiteSpace(options.IndexFormat))
        {
            errors.Add("IndexFormat is required when Elasticsearch logging is enabled.");
        }

        if (!TryParseMinimumLevel(options.MinimumLevel, out _))
        {
            errors.Add($"MinimumLevel '{options.MinimumLevel}' is not a supported Serilog level.");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    internal static bool TryParseMinimumLevel(string? value, out Serilog.Events.LogEventLevel level)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            level = Serilog.Events.LogEventLevel.Information;
            return false;
        }

        return Enum.TryParse(value.Trim(), ignoreCase: true, out level);
    }
}
