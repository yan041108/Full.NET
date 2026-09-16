using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Oidc;

namespace Full.NET.Modules.Identity.Features.ManageOidcSigningKeys;

/// <summary>在已配置密钥材料范围内激活 OIDC 签名密钥，并同步 OpenIddict 运行时凭据。</summary>
internal sealed class OidcSigningKeyManagementService(
    IdentityOidcSigningKeyRing keyRing,
    IdentityOidcOpenIddictSigningCredentialSynchronizer credentialSynchronizer,
    OidcSigningKeyQueryService queries,
    OidcManagementAuditWriter auditWriter)
{
    public async Task<Result<OidcSigningKeyListResponse>> ActivateAsync(
        string keyId,
        OidcManagementActorContext actor,
        CancellationToken cancellationToken = default)
    {
        var activation = keyRing.TryActivate(keyId);
        if (!activation.IsSuccess)
        {
            return Result<OidcSigningKeyListResponse>.Failure(activation.Error!);
        }

        credentialSynchronizer.Sync();
        await auditWriter.WriteAsync(
                actor.ActorUserId,
                OidcManagementAuditWriter.SigningKeyActivatedEventType,
                $"key:{keyId}",
                actor.IpAddress,
                actor.UserAgent,
                cancellationToken)
            .ConfigureAwait(false);
        return queries.List();
    }
}