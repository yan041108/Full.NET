using System.Globalization;
using Microsoft.Extensions.Configuration;
using OpenTelemetry.Exporter;

namespace Full.NET.Hosting.Observability;

/// <summary>在没有全局出口时，按显式信号端点注册导出，配置错误在注册阶段失败。</summary>
internal static class SignalOtlpConfiguration
{
    internal static Action<OtlpExporterOptions>? Create(IConfiguration configuration, string signal)
    {
        var prefix = $"OTEL_EXPORTER_OTLP_{signal}_";
        var address = configuration[prefix + "ENDPOINT"];
        if (string.IsNullOrWhiteSpace(address)) return null;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(endpoint.Host) || !string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Fragment))
            throw Invalid(prefix + "ENDPOINT");

        // AddOtlpExporter 只读取通用环境键；显式映射信号配置，优先级与 OTLP 规范一致。
        string? Setting(string suffix) => configuration[prefix + suffix]
            ?? configuration["OTEL_EXPORTER_OTLP_" + suffix];
        var protocolValue = Setting("PROTOCOL");
        OtlpExportProtocol? protocol = protocolValue switch
        {
            null => null,
            "http/protobuf" => OtlpExportProtocol.HttpProtobuf,
            // SDK 仍支持 gRPC；保留标准协议选择，不忽略任何裁剪或 AOT 告警。
#pragma warning disable CS0618
            "grpc" => OtlpExportProtocol.Grpc,
#pragma warning restore CS0618
            _ => throw Invalid(prefix + "PROTOCOL")
        };
        var timeoutValue = Setting("TIMEOUT");
        int? timeout = null;
        if (timeoutValue is not null)
        {
            if (!int.TryParse(timeoutValue, NumberStyles.None, CultureInfo.InvariantCulture, out var milliseconds)
                || milliseconds <= 0) throw Invalid(prefix + "TIMEOUT");
            timeout = milliseconds;
        }
        var compressionValue = Setting("COMPRESSION");
        OtlpExportCompression? compression = compressionValue switch
        {
            null => null,
            "none" => OtlpExportCompression.None,
            "gzip" => OtlpExportCompression.GZip,
            _ => throw Invalid(prefix + "COMPRESSION")
        };
        var headers = Setting("HEADERS");
        return options =>
        {
            // 信号端点是完整 URL，赋值后 SDK 不再拼接 /v1/metrics 或 /v1/traces。
            options.Endpoint = endpoint;
            if (protocol.HasValue) options.Protocol = protocol.Value;
            if (timeout.HasValue) options.TimeoutMilliseconds = timeout.Value;
            if (compression.HasValue) options.Compression = compression.Value;
            if (headers is not null) options.Headers = headers;
        };
    }

    // 错误只包含配置键，不能回显可能含凭据的地址或 Header。
    private static InvalidOperationException Invalid(string key) =>
        new($"Invalid OTLP configuration: {key}.");
}
