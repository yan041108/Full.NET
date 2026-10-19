using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Security;

/// <summary>后台绑定分别重查会话与租户活动的权威状态，不把 ClaimsPrincipal 持久化或作为长期授权。</summary>
internal sealed class BackgroundSessionBindingValidator(
    IQueryExecutor queryExecutor,
    IClock clock,
    IOptions<IdentityOptions> identityOptions,
    IIdentityActiveTenantDirectory activeTenants)
    : IBackgroundSessionBindingValidator
{
    private readonly IdentityOptions _identityOptions = identityOptions.Value;

    public async Task<bool> IsValidAsync(SessionBindingSnapshot binding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        cancellationToken.ThrowIfCancellationRequested();
        if (binding.UserId == Guid.Empty || binding.SessionId == Guid.Empty
            || string.IsNullOrWhiteSpace(binding.SecurityStamp)
            || string.IsNullOrWhiteSpace(binding.ActorScope)
            || string.IsNullOrWhiteSpace(binding.EffectiveScope))
        {
            return false;
        }

        if (string.Equals(binding.SessionKind, SessionBindingKinds.OidcApplication, StringComparison.Ordinal))
        {
            var sessionValid = await ValidateOidcApplicationBindingAsync(binding, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return sessionValid && await ValidateTenantActivityAsync(binding.TenantId, cancellationToken)
                .ConfigureAwait(false);
        }

        if (!string.Equals(binding.SessionKind, SessionBindingKinds.Refresh, StringComparison.Ordinal))
        {
            return false;
        }

        RefreshSessionRecord? record;
        try
        {
            record = await queryExecutor.QuerySingleOrDefaultAsync<RefreshSessionRecord>(
                    IdentitySql.FindRefreshSessionById,
                    new Dictionary<string, object?> { ["SessionId"] = binding.SessionId },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return ValidateRefreshBinding(record, binding)
            && await ValidateTenantActivityAsync(binding.TenantId, cancellationToken).ConfigureAwait(false);
    }

    // 会话与租户启停属于不同权威事实；后台不能沿用领取运行时的租户快照或依赖缓存通知。
    private async Task<bool> ValidateTenantActivityAsync(Guid? tenantId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (tenantId is not { } id) return true;
        try
        {
            var active = await activeTenants.IsActiveTenantAsync(id, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return active;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // 与会话表读取保持一致：权威结果未知时拒绝绑定，不退回先前活动状态。
            return false;
        }
    }

    private async Task<bool> ValidateOidcApplicationBindingAsync(
        SessionBindingSnapshot binding,
        CancellationToken cancellationToken)
    {
        IdentityOidcApplicationSessionValidationRecord? record;
        try
        {
            record = await queryExecutor.QuerySingleOrDefaultAsync<IdentityOidcApplicationSessionValidationRecord>(
                    IdentityOidcSessionSql.FindApplicationSessionValidationById,
                    IdentitySqlParameters.Create(("ApplicationSessionId", binding.SessionId)),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }

        if (record is null
            || record.UserId != binding.UserId
            || !record.IsActive
            || record.ApplicationRevokedAtUtc.HasValue
            || record.ApplicationExpiresAtUtc <= clock.UtcNow
            || record.CenterRevokedAtUtc.HasValue
            || record.CenterExpiresAtUtc <= clock.UtcNow
            || record.LockoutEndUtc > clock.UtcNow
            || !string.Equals(record.CenterSecurityStamp, record.UserSecurityStamp, StringComparison.Ordinal)
            || !string.Equals(record.CenterSecurityStamp, binding.SecurityStamp, StringComparison.Ordinal)
            || !string.Equals(record.ActorScope, binding.ActorScope, StringComparison.Ordinal)
            || !string.Equals(record.EffectiveScope, binding.EffectiveScope, StringComparison.Ordinal)
            || PasswordChangeRequirementEvaluator.IsRequired(
                record.MustChangePassword,
                record.PasswordChangedAtUtc,
                clock.UtcNow,
                _identityOptions.PasswordExpirationDays))
        {
            return false;
        }

        return binding.TenantId.HasValue
            ? record.ActiveTenantId == binding.TenantId
            : record.ActiveTenantId is null;
    }

    private bool ValidateRefreshBinding(RefreshSessionRecord? record, SessionBindingSnapshot binding)
    {
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
