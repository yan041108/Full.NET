using Full.NET.Abstractions.Time;
using OpenIddict.Abstractions;

namespace Full.NET.Modules.Identity.Oidc;

/// <summary>借助持久化令牌的乐观并发状态原子消费一次性交互同意。</summary>
internal sealed class IdentityOidcTenantConsentTokenStore(IOpenIddictTokenManager tokens, IClock clock)
{
    private const string ConsentType = "fullnet_tenant_consent";

    public async Task<Guid> CreateAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty) throw new ArgumentException("用户标识不能为空。", nameof(userId));
        var token = await tokens.CreateAsync(new OpenIddictTokenDescriptor
        {
            Type = ConsentType,
            Status = OpenIddictConstants.Statuses.Valid,
            Subject = userId.ToString("D"),
            CreationDate = clock.UtcNow,
            ExpirationDate = clock.UtcNow.AddMinutes(5),
        }, cancellationToken).ConfigureAwait(false);
        if (!Guid.TryParse(await tokens.GetIdAsync(token, cancellationToken).ConfigureAwait(false), out var id)
            || id == Guid.Empty)
            throw new InvalidOperationException("同意令牌缺少有效标识。");
        return id;
    }

    public async Task<bool> TryConsumeAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty || userId == Guid.Empty) return false;
        var token = await tokens.FindByIdAsync(id.ToString("D"), cancellationToken).ConfigureAwait(false);
        if (token is null
            || !string.Equals(await tokens.GetTypeAsync(token, cancellationToken).ConfigureAwait(false), ConsentType, StringComparison.Ordinal)
            || !string.Equals(await tokens.GetStatusAsync(token, cancellationToken).ConfigureAwait(false), OpenIddictConstants.Statuses.Valid, StringComparison.Ordinal)
            || !string.Equals(await tokens.GetSubjectAsync(token, cancellationToken).ConfigureAwait(false), userId.ToString("D"), StringComparison.Ordinal)
            || await tokens.GetExpirationDateAsync(token, cancellationToken).ConfigureAwait(false) is not { } expiry
            || expiry <= clock.UtcNow)
            return false;
        return await tokens.TryRedeemAsync(token, cancellationToken).ConfigureAwait(false);
    }
}
