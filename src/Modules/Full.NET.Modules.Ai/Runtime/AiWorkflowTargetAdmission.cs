using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Features.ManageChatSessions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Runtime;

/// <summary>目标聊天资源与原登录会话分别核验，后台派发不得沿用创建时的权限快照。</summary>
internal sealed class AiWorkflowTargetAdmission(IBackgroundSessionAuthorization authorization,
    AiChatSessionQueryService queries, ICurrentTenant tenant,
    Full.NET.Modules.Calendar.Contracts.IPersonalScheduleCompletion? calendar = null)
{
    internal async Task<bool> AuthorizeAsync(AgentRunBudgetSnapshot snapshot, SessionBindingSnapshot binding, CancellationToken token)
    {
        if (snapshot.PersonalScheduleTarget is { } schedule)
            return snapshot.TargetChatSessionId is null && calendar is not null
                && await calendar.CanCompleteAsync(schedule.Id, schedule.Version, binding, token).ConfigureAwait(false);
        return snapshot.TargetChatSessionId is { } target && await AuthorizeAsync(target, binding, token).ConfigureAwait(false);
    }
    internal async Task<bool> AuthorizeAsync(Guid target, SessionBindingSnapshot binding, CancellationToken token) =>
        await ReadAsync(target, binding, token).ConfigureAwait(false) is not null;

    internal async Task<AiChatSessionRecord?> ReadAsync(Guid target, SessionBindingSnapshot binding, CancellationToken token)
    {
        if (target == Guid.Empty || binding.TenantId != tenant.Id || (!tenant.IsHost && !tenant.IsAvailable)) return null;
        var actor = await authorization.AuthorizeAllAsync(binding, [AiChatPermissions.Read, AiChatPermissions.Update], token).ConfigureAwait(false);
        if (actor?.UserId != binding.UserId || actor.TenantId != binding.TenantId || actor.SessionId != binding.SessionId) return null;
        return await queries.FindOwnedSessionAsync(AiChatScope.Resolve(tenant), target, binding.UserId, token).ConfigureAwait(false);
    }
}
