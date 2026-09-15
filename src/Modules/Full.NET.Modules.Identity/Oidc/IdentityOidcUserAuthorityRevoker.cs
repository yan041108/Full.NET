using Full.NET.Modules.Identity.Configuration;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Oidc;

/// <summary>账号权威状态变化时撤销 OIDC 中心/应用会话与 OpenIddict 授权。</summary>
internal interface IIdentityOidcUserAuthorityRevoker
{
    Task RevokeUserAuthorityAsync(Guid userId, CancellationToken cancellationToken = default);
}

internal sealed class NullIdentityOidcUserAuthorityRevoker : IIdentityOidcUserAuthorityRevoker
{
    public Task RevokeUserAuthorityAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

internal sealed class IdentityOidcUserAuthorityRevoker(
    IOptions<IdentityOidcOptions> options,
    IdentityOidcSessionService sessionService,
    IdentityOidcGrantRevocationService grantRevocationService) : IIdentityOidcUserAuthorityRevoker
{
    public async Task RevokeUserAuthorityAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enable)
        {
            return;
        }

        await sessionService.RevokeAllApplicationSessionsByUserAsync(userId, cancellationToken)
            .ConfigureAwait(false);
        await sessionService.RevokeAllCenterSessionsByUserAsync(userId, cancellationToken)
            .ConfigureAwait(false);
        await grantRevocationService.RevokeByUserIdAsync(userId, cancellationToken)
            .ConfigureAwait(false);
    }
}