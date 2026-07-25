using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace Full.NET.Realtime.SignalR;

/// <summary>
/// 允许浏览器 SignalR 客户端通过查询字符串传递访问令牌，同时保留 Identity 既有 JWT 事件链。
/// </summary>
internal sealed class JwtBearerSignalRAccessTokenPostConfigure(
    IOptions<RealtimeOptions> realtimeOptions)
    : IPostConfigureOptions<JwtBearerOptions>
{
    public void PostConfigure(string? name, JwtBearerOptions options)
    {
        if (!string.Equals(name, JwtBearerDefaults.AuthenticationScheme, StringComparison.Ordinal))
        {
            return;
        }

        var hubPath = realtimeOptions.Value.HubPath;
        if (string.IsNullOrWhiteSpace(hubPath))
        {
            return;
        }

        var previous = options.Events.OnMessageReceived;
        options.Events.OnMessageReceived = async context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken)
                && path.StartsWithSegments(hubPath, StringComparison.OrdinalIgnoreCase))
            {
                context.Token = accessToken;
            }
            else if (previous is not null)
            {
                await previous(context).ConfigureAwait(false);
            }
        };
    }
}
