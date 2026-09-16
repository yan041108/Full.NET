using System.Security.Claims;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.Modules.Identity.Oidc;

internal sealed class IdentityOidcAccessSessionValidator(
    IQueryExecutor queryExecutor,
    IClock clock,
    ICurrentTenantContextWriter tenantContextWriter,
    IOptions<IdentityOidcOptions> oidcOptions,
    IOptions<IdentityOptions> identityOptions)
{
    private readonly IdentityOidcOptions _oidcOptions = oidcOptions.Value;
    private readonly IdentityOptions _identityOptions = identityOptions.Value;

    public async Task<bool> IsValidAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (!_oidcOptions.Enable
            || string.IsNullOrWhiteSpace(_oidcOptions.Issuer)
            || !TryReadOidcClaims(principal, out var userId, out var applicationSessionId, out var tokenUse))
        {
            return false;
        }

        var issuer = principal.FindFirstValue(JwtRegisteredClaimNames.Iss);
        if (!string.Equals(issuer, _oidcOptions.Issuer, StringComparison.Ordinal))
        {
            return false;
        }

        if (!IsAudienceValid(principal))
        {
            return false;
        }

        if (!string.Equals(tokenUse, IdentityOidcPrincipalFactory.TokenUseAccess, StringComparison.Ordinal))
        {
            return false;
        }

        // 权威表为 HostOnly，但工具执行期间也会复验会话；查询后必须恢复调用者的可信上下文。
        var wasHost = tenantContextWriter.IsHost;
        var previousTenant = tenantContextWriter.Id is Guid tenantId
            ? new TenantContext(tenantId, tenantContextWriter.Identifier!, tenantContextWriter.Name!)
            : null;
        tenantContextWriter.SetHost();
        IdentityOidcApplicationSessionValidationRecord? record;
        try
        {
            record = await queryExecutor.QuerySingleOrDefaultAsync<IdentityOidcApplicationSessionValidationRecord>(
                    IdentityOidcSessionSql.FindApplicationSessionValidationById,
                    IdentitySqlParameters.Create(("ApplicationSessionId", applicationSessionId)),
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
        finally
        {
            if (previousTenant is not null)
            {
                tenantContextWriter.SetTenant(previousTenant);
            }
            else if (wasHost)
            {
                tenantContextWriter.SetHost();
            }
            else
            {
                tenantContextWriter.Clear();
            }
        }

        if (!IsActive(record, userId, clock.UtcNow))
        {
            return false;
        }

        var actorScope = principal.FindFirstValue(IdentityClaimTypes.ActorScope) ?? string.Empty;
        var effectiveScope = principal.FindFirstValue(IdentityClaimTypes.Scope) ?? string.Empty;
        if (!string.Equals(actorScope, record!.ActorScope, StringComparison.Ordinal)
            || !string.Equals(effectiveScope, record.EffectiveScope, StringComparison.Ordinal))
        {
            return false;
        }

        var tenantClaim = principal.FindFirstValue(IdentityClaimTypes.TenantId);
        return record.ActiveTenantId.HasValue
            ? Guid.TryParse(tenantClaim, out var tokenTenantId)
                && tokenTenantId == record.ActiveTenantId.Value
            : string.IsNullOrEmpty(tenantClaim);
    }

    private bool IsAudienceValid(ClaimsPrincipal principal)
    {
        var audiences = principal.FindAll(JwtRegisteredClaimNames.Aud)
            .Select(claim => claim.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        if (audiences.Length == 0)
        {
            var legacyAudience = principal.FindFirstValue(JwtRegisteredClaimNames.Aud);
            if (!string.IsNullOrWhiteSpace(legacyAudience))
            {
                audiences = [legacyAudience];
            }
        }

        return audiences.Any(audience =>
            string.Equals(audience, _identityOptions.Audience, StringComparison.Ordinal));
    }

    private static bool TryReadOidcClaims(
        ClaimsPrincipal principal,
        out Guid userId,
        out Guid applicationSessionId,
        out string tokenUse)
    {
        userId = Guid.Empty;
        applicationSessionId = Guid.Empty;
        tokenUse = principal.FindFirstValue(FullNetIdentityClaimTypes.TokenUse) ?? string.Empty;
        return Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId)
            && Guid.TryParse(
                principal.FindFirstValue(FullNetIdentityClaimTypes.ApplicationSessionId),
                out applicationSessionId);
    }

    private static bool IsActive(
        IdentityOidcApplicationSessionValidationRecord? record,
        Guid userId,
        DateTimeOffset now) =>
        record is not null
        && record.UserId == userId
        && record.IsActive
        && !record.ApplicationRevokedAtUtc.HasValue
        && record.ApplicationExpiresAtUtc > now
        && !record.CenterRevokedAtUtc.HasValue
        && record.CenterExpiresAtUtc > now
        && !(record.LockoutEndUtc > now)
        && string.Equals(record.CenterSecurityStamp, record.UserSecurityStamp, StringComparison.Ordinal);
}
