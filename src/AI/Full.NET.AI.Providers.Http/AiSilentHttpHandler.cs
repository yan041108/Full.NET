using System.Diagnostics.Metrics;
using OpenTelemetry;

namespace Full.NET.AI.Providers.Http;

/// <summary>静默通道同时抑制宿主通用 HTTP 追踪；不修改进程级诊断开关或其他业务请求。</summary>
internal sealed class AiSilentHttpHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var suppression = SuppressInstrumentationScope.Begin();
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>为静默传输提供已关闭的专属 Meter；后续创建的工具也不会发布给通用 HTTP 监听器。</summary>
internal sealed class AiSilentMeterFactory : IMeterFactory
{
    public Meter Create(MeterOptions options)
    {
        var meter = new Meter(options); meter.Dispose(); return meter;
    }
    public void Dispose() { }
}
