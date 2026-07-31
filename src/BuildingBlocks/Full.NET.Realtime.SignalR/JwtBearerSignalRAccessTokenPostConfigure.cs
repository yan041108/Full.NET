using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
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

        // JwtBearerHandler 在 EventsType 存在时会完全忽略 options.Events，因此必须把类型化事件
        // 纳入同一适配器，避免为 SignalR 接入查询令牌时绕过 Identity 的会话即时校验。
        var configuredEvents = options.Events;
        var configuredEventsType = options.EventsType;
        options.EventsType = null;
        options.Events = new SignalRAccessTokenJwtBearerEvents(
            hubPath,
            configuredEvents,
            configuredEventsType);
    }
}

/// <summary>
/// 仅在 Hub 路径读取查询令牌，并把其余 JWT 生命周期事件转发给原有事件实现。
/// </summary>
internal sealed class SignalRAccessTokenJwtBearerEvents(
    string hubPath,
    JwtBearerEvents configuredEvents,
    Type? configuredEventsType) : JwtBearerEvents
{
    private readonly string _negotiatePath =
        $"{hubPath}/negotiate";

    public override async Task MessageReceived(MessageReceivedContext context)
    {
        await ResolveConfiguredEvents(context.HttpContext)
            .MessageReceived(context)
            .ConfigureAwait(false);
        if (!string.IsNullOrEmpty(context.Token)
            || context.Result is not null)
        {
            return;
        }

        var accessToken = context.Request.Query["access_token"];
        if (accessToken.Count == 1
            && !string.IsNullOrEmpty(accessToken)
            && IsHubTransportPath(context.Request.Path.Value))
        {
            // 多值查询会被 StringValues 隐式拼接，必须在进入 JWT 解析前拒绝歧义令牌。
            context.Token = accessToken;
        }
    }

    public override Task TokenValidated(TokenValidatedContext context) =>
        ResolveConfiguredEvents(context.HttpContext).TokenValidated(context);

    public override Task AuthenticationFailed(
        AuthenticationFailedContext context) =>
        ResolveConfiguredEvents(context.HttpContext)
            .AuthenticationFailed(context);

    public override Task Challenge(JwtBearerChallengeContext context) =>
        ResolveConfiguredEvents(context.HttpContext).Challenge(context);

    public override Task Forbidden(ForbiddenContext context) =>
        ResolveConfiguredEvents(context.HttpContext).Forbidden(context);

    private bool IsHubTransportPath(string? requestPath) =>
        string.Equals(
            requestPath,
            hubPath,
            StringComparison.OrdinalIgnoreCase)
        || string.Equals(
            requestPath,
            _negotiatePath,
            StringComparison.OrdinalIgnoreCase);

    private JwtBearerEvents ResolveConfiguredEvents(
        Microsoft.AspNetCore.Http.HttpContext httpContext)
    {
        if (configuredEventsType is null)
        {
            return configuredEvents;
        }

        return (JwtBearerEvents)httpContext.RequestServices
            .GetRequiredService(configuredEventsType);
    }
}
