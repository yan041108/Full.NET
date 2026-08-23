using Full.NET.Realtime.SignalR.Features.RealtimeProbe;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Realtime.SignalR;

/// <summary>
/// 映射 SignalR Hub 与 Testing 探针端点。
/// </summary>
public static class WebApplicationExtensions
{
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
