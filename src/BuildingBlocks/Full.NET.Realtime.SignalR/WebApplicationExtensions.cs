using Full.NET.Realtime.SignalR.Features.RealtimeProbe;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Realtime.SignalR;

/// <summary>
/// 映射 SignalR Hub 与 Testing 探针端点。
/// </summary>
public static class WebApplicationExtensions
{
    /// <summary>
    /// 映射 SignalR Hub 与实时探针端点；<see cref="RealtimeOptions.Enabled"/> 为 false 时跳过注册。
    /// </summary>
    /// <param name="app">Web 应用程序。</param>
    /// <returns>链式返回 <paramref name="app"/>。</returns>
    public static WebApplication MapFullNetRealtime(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var options = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<RealtimeOptions>>().Value;
        if (!options.Enabled)
        {
            return app;
        }

        app.MapHub<FullNetNotificationHub>(options.HubPath);
        RealtimeProbeEndpoint.Map(app, app.Environment);
        return app;
    }
}
