using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Security;

/// <summary>与交互 JWT 校验共用会话表权威状态，不把 ClaimsPrincipal 持久化到运行记录。</summary>
internal sealed class BackgroundSessionBindingValidator(IQueryExecutor queryExecutor, IClock clock)
    : IBackgroundSessionBindingValidator
{
    public async Task<bool> IsValidAsync(SessionBindingSnapshot binding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (binding.UserId == Guid.Empty || binding.SessionId == Guid.Empty
            || string.IsNullOrWhiteSpace(binding.SecurityStamp)
            || string.IsNullOrWhiteSpace(binding.ActorScope)
            || string.IsNullOrWhiteSpace(binding.EffectiveScope))
        {
            return false;
        }

        var record = await queryExecutor.QuerySingleOrDefaultAsync<RefreshSessionRecord>(
                IdentitySql.FindRefreshSessionById,
                new Dictionary<string, object?> { ["SessionId"] = binding.SessionId },
                cancellationToken)
            .ConfigureAwait(false);
        if (!IsActive(record, binding.UserId, binding.SecurityStamp))
        {
            return false;
        }

        var effectiveTenantId = record!.ActiveTenantId ?? record.TenantId;
        var expectedScope = effectiveTenantId.HasValue
            ? FormattableString.Invariant($"tenant:{effectiveTenantId.Value:N}")
            : record.ScopeKey;
        if (!string.Equals(binding.ActorScope, record.ScopeKey, StringComparison.Ordinal)
            || !string.Equals(binding.EffectiveScope, expectedScope, StringComparison.Ordinal))
        {
            return false;
        }

        return binding.TenantId.HasValue
            ? effectiveTenantId == binding.TenantId
            : effectiveTenantId is null;
    }

    private bool IsActive(RefreshSessionRecord? record, Guid userId, string securityStamp) =>
        record is not null
        && record.UserId == userId
        && record.IsActive
        && record.ExpiresAtUtc > clock.UtcNow
        && !record.ConsumedAtUtc.HasValue
        && !record.RevokedAtUtc.HasValue
        && !(record.LockoutEndUtc > clock.UtcNow)
        && string.Equals(record.SecurityStamp, securityStamp, StringComparison.Ordinal);
}
