using System.Security.Claims;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.Modules.Identity.Security;

/// <summary>
/// 依据数据库中的会话与账号状态校验已签名的访问令牌，保证撤销、轮换和上下文切换立即生效。
/// </summary>
internal sealed class AccessSessionValidator(
    IQueryExecutor queryExecutor,
    IClock clock)
{
    public async Task<bool> IsValidAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (!TryReadRequiredClaims(
                principal,
                out var userId,
                out var sessionId,
                out var securityStamp,
                out var actorScope,
                out var effectiveScope))
        {
            return false;
        }

        IReadOnlyDictionary<string, object?> parameters =
            new Dictionary<string, object?>
            {
                ["SessionId"] = sessionId,
            };
        var record = await queryExecutor.QuerySingleOrDefaultAsync<RefreshSessionRecord>(
                IdentitySql.FindRefreshSessionById,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        if (!IsActive(record, userId, securityStamp))
        {
            return false;
        }

        var effectiveTenantId = record!.ActiveTenantId ?? record.TenantId;
        var expectedScope = effectiveTenantId.HasValue
            ? $"tenant:{effectiveTenantId.Value:N}"
            : record.ScopeKey;
        if (!string.Equals(actorScope, record.ScopeKey, StringComparison.Ordinal)
            || !string.Equals(effectiveScope, expectedScope, StringComparison.Ordinal))
        {
            return false;
        }

        var tenantClaim = principal.FindFirstValue(IdentityClaimTypes.TenantId);
        return effectiveTenantId.HasValue
            ? Guid.TryParse(tenantClaim, out var tokenTenantId)
                && tokenTenantId == effectiveTenantId.Value
            : string.IsNullOrEmpty(tenantClaim);
    }

    private bool IsActive(
        RefreshSessionRecord? record,
        Guid userId,
        string securityStamp) =>
        record is not null
        && record.UserId == userId
        && record.IsActive
        && record.ExpiresAtUtc > clock.UtcNow
        && !record.ConsumedAtUtc.HasValue
        && !record.RevokedAtUtc.HasValue
        && !(record.LockoutEndUtc > clock.UtcNow)
        && string.Equals(
            record.SecurityStamp,
            securityStamp,
            StringComparison.Ordinal);

    private static bool TryReadRequiredClaims(
        ClaimsPrincipal principal,
        out Guid userId,
        out Guid sessionId,
        out string securityStamp,
        out string actorScope,
        out string effectiveScope)
    {
        userId = Guid.Empty;
        sessionId = Guid.Empty;
        securityStamp = principal.FindFirstValue(IdentityClaimTypes.SecurityStamp)
            ?? string.Empty;
        actorScope = principal.FindFirstValue(IdentityClaimTypes.ActorScope)
            ?? string.Empty;
        effectiveScope = principal.FindFirstValue(IdentityClaimTypes.Scope)
            ?? string.Empty;
        return Guid.TryParse(
                principal.FindFirstValue(JwtRegisteredClaimNames.Sub),
                out userId)
            && Guid.TryParse(
                principal.FindFirstValue(IdentityClaimTypes.SessionId),
                out sessionId)
            && !string.IsNullOrWhiteSpace(securityStamp)
            && !string.IsNullOrWhiteSpace(actorScope)
            && !string.IsNullOrWhiteSpace(effectiveScope);
    }
}
