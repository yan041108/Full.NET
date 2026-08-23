using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Full.NET.Realtime.SignalR;

/// <summary>
/// Host/租户管理端通知 Hub：只负责连接、鉴权与分组，不承载业务规则。
/// </summary>
[Authorize]
public sealed class FullNetNotificationHub : Hub
{
    private static readonly object ActiveConnectionMarker = new();

    /// <summary>
    /// 连接建立后按已验证身份加入用户组与可选租户组。
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var isAuthorized = TryResolveGroups(
            Context.User,
            out var userId,
            out var broadcastGroup,
            out var authorizationOutcome);
        RealtimeHubTelemetry.RecordAuthorizationDecision(
            authorizationOutcome);
        if (isAuthorized)
        {
            await AddToGroupAsync(
                    RealtimeGroups.User(userId),
                    "user")
                .ConfigureAwait(false);
            await AddToGroupAsync(
                    broadcastGroup,
                    "broadcast")
                .ConfigureAwait(false);
        }
        else
        {
            // 已认证但缺少自洽 Full.NET 作用域的连接不能留在 Hub 内空转，
            // 否则异常认证方案可持续占用连接资源并绕过作用域失败关闭边界。
            Context.Abort();
        }

        await base.OnConnectedAsync().ConfigureAwait(false);
        if (isAuthorized)
        {
            Context.Items[ActiveConnectionMarker] =
                Stopwatch.GetTimestamp();
            RealtimeHubTelemetry.RecordActiveConnection(1);
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        try
        {
            await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
        }
        finally
        {
            // Items 属于连接生命周期，可跨 Hub 瞬态实例识别已计数的授权连接。
            if (Context.Items.Remove(
                    ActiveConnectionMarker,
                    out var startedMarker)
                && startedMarker is long startedTimestamp)
            {
                RealtimeHubTelemetry.RecordActiveConnection(-1);
                RealtimeHubTelemetry.RecordConnectionDuration(
                    startedTimestamp,
                    exception is null
                        ? "completed"
                        : "failure");
            }
        }
    }

    private async Task AddToGroupAsync(
        string group,
        string target)
    {
        var startedTimestamp = Stopwatch.GetTimestamp();
        var outcome = "success";
        try
        {
            await Groups.AddToGroupAsync(
                    Context.ConnectionId,
                    group,
                    Context.ConnectionAborted)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (Context.ConnectionAborted.IsCancellationRequested)
        {
            outcome = "canceled";
            throw;
        }
        catch (Exception)
        {
            outcome = "failure";
            throw;
        }
        finally
        {
            RealtimeHubTelemetry.RecordGroupAssignment(
                startedTimestamp,
                target,
                outcome);
        }
    }

    private static bool TryResolveGroups(
        ClaimsPrincipal? user,
        out Guid userId,
        out string broadcastGroup,
        out string authorizationOutcome)
    {
        userId = default;
        broadcastGroup = string.Empty;
        authorizationOutcome =
            "rejected_invalid_subject";
        if (!TryGetSingleClaimValue(
                user,
                "sub",
                out var subjectClaim)
            || !Guid.TryParse(subjectClaim, out userId)
            || userId == Guid.Empty)
        {
            return false;
        }

        // Claim 名称与 Identity 公共契约保持一致，但 BuildingBlock 不反向依赖业务模块。
        // 所有组成员关系必须由显式且自洽的 Full.NET 作用域决定，异常主体一律失败关闭。
        authorizationOutcome =
            "rejected_scope_claim_mismatch";
        if (!TryGetSingleClaimValue(
                user,
                "fullnet_scope",
                out var effectiveScope))
        {
            return false;
        }

        var hasTenantClaim = user?.HasClaim(claim =>
            string.Equals(
                claim.Type,
                "fullnet_tenant_id",
                StringComparison.Ordinal)) == true;
        if (string.Equals(
                effectiveScope,
                "host",
                StringComparison.Ordinal)
            && !hasTenantClaim)
        {
            broadcastGroup = RealtimeGroups.HostBroadcast;
            authorizationOutcome = "authorized_host";
            return true;
        }

        if (TryGetSingleClaimValue(
                user,
                "fullnet_tenant_id",
                out var tenantClaim)
            && Guid.TryParse(tenantClaim, out var tenantId)
            && tenantId != Guid.Empty
            && string.Equals(
                effectiveScope,
                $"tenant:{tenantId:N}",
                StringComparison.Ordinal))
        {
            broadcastGroup = RealtimeGroups.Tenant(tenantId);
            authorizationOutcome = "authorized_tenant";
            return true;
        }

        return false;
    }

    private static bool TryGetSingleClaimValue(
        ClaimsPrincipal? user,
        string claimType,
        out string value)
    {
        value = string.Empty;
        if (user is null)
        {
            return false;
        }

        using var claims = user.FindAll(claimType).GetEnumerator();
        if (!claims.MoveNext())
        {
            return false;
        }

        var candidate = claims.Current.Value;
        if (claims.MoveNext())
        {
            // 安全 Claim 必须保持单值；重复的相同值也不能依赖顺序静默授权。
            return false;
        }

        value = candidate;
        return true;
    }
}
