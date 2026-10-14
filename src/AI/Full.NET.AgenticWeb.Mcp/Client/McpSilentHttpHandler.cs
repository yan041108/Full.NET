using System.Diagnostics.Metrics;
using OpenTelemetry;

namespace Full.NET.AgenticWeb.Mcp.Client;

/// <summary>MCP 出站诊断禁用时抑制通用 HTTP 追踪；作用域不会影响其他请求。</summary>
internal sealed class McpSilentHttpHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var scope = SuppressInstrumentationScope.Begin();
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>静默 MCP 传输独占已关闭 Meter，不发布通用网络指标。</summary>
internal sealed class McpSilentMeterFactory : IMeterFactory
{
    public Meter Create(MeterOptions options)
    { var meter = new Meter(options); meter.Dispose(); return meter; }
    public void Dispose() { }
}
