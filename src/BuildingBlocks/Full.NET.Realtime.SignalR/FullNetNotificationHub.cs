using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Full.NET.Realtime.SignalR;

/// <summary>
/// Host/租户管理端通知 Hub：只负责连接、鉴权与分组，不承载业务规则。
/// </summary>
[Authorize]
public sealed class FullNetNotificationHub : Hub<IFullNetNotificationClient>
{
    /// <summary>
    /// 连接建立后按已验证身份加入用户组与可选租户组。
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        if (Context.User is not null)
        {
            var subject = Context.User.FindFirst("sub")?.Value;
            if (Guid.TryParse(subject, out var userId))
            {
                await Groups.AddToGroupAsync(
                        Context.ConnectionId,
                        RealtimeGroups.User(userId),
                        Context.ConnectionAborted)
                    .ConfigureAwait(false);
            }

            // 与 FullNetIdentityClaimTypes.TenantId 保持一致，避免 BuildingBlocks 依赖模块契约项目。
            var tenantClaim = Context.User.FindFirst("fullnet_tenant_id")?.Value;
            if (Guid.TryParse(tenantClaim, out var tenantId))
            {
                await Groups.AddToGroupAsync(
                        Context.ConnectionId,
                        RealtimeGroups.Tenant(tenantId),
                        Context.ConnectionAborted)
                    .ConfigureAwait(false);
            }
            else
            {
                await Groups.AddToGroupAsync(
                        Context.ConnectionId,
                        RealtimeGroups.HostBroadcast,
                        Context.ConnectionAborted)
                    .ConfigureAwait(false);
            }
        }

        await base.OnConnectedAsync().ConfigureAwait(false);
    }
}
