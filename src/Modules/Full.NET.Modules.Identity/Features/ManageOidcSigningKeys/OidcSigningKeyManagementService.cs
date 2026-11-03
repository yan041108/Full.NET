using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Data.Abstractions;
using Full.NET.Abstractions.Messaging;

namespace Full.NET.Modules.Identity.Features.ManageOidcSigningKeys;

/// <summary>在已配置密钥材料范围内激活 OIDC 签名密钥，并同步 OpenIddict 运行时凭据。</summary>
internal sealed class OidcSigningKeyManagementService(
    IdentityOidcSigningKeyRing keyRing,
    IdentityOidcSigningPolicy signingPolicy,
    ICommandTransaction transaction,
    OidcSigningKeyQueryService queries,
    OidcManagementAuditWriter auditWriter,
    IdentityOidcKeyGovernance governance)
{
    public Task<Result<OidcSigningKeyListResponse>> PublishAsync(string keyId, OidcManagementActorContext actor, CancellationToken token) =>
        ChangeLifecycleAsync(keyId, actor, publish: true, token);
    public Task<Result<OidcSigningKeyListResponse>> RetireAsync(string keyId, OidcManagementActorContext actor, CancellationToken token) =>
        ChangeLifecycleAsync(keyId, actor, publish: false, token);

    private async Task<Result<OidcSigningKeyListResponse>> ChangeLifecycleAsync(string keyId, OidcManagementActorContext actor,
        bool publish, CancellationToken token)
    {
        var changed = await transaction.ExecuteResultAsync(async ct =>
        {
            var allowed = publish ? await governance.PublishAsync(keyId, ct).ConfigureAwait(false)
                : await governance.RetireAsync(keyId, ct).ConfigureAwait(false);
            if (!allowed) return Result<bool>.Failure(new Error(IdentityErrorCodes.OidcSigningKeyLifecycleConflict,
                "The signing key lifecycle transition is not allowed.", ErrorType.Conflict));
            await auditWriter.WriteAsync(actor.ActorUserId, publish ? "oidc.signing_key_published" : "oidc.signing_key_retired",
                $"key:{keyId}", actor.IpAddress, actor.UserAgent, ct).ConfigureAwait(false);
            return Result<bool>.Success(true);
        }, token).ConfigureAwait(false);
        if (!changed.IsSuccess) return Result<OidcSigningKeyListResponse>.Failure(changed.Error!);
        await signingPolicy.SynchronizeAsync(token).ConfigureAwait(false);
        return await queries.ListAsync(token).ConfigureAwait(false);
    }

    public async Task<Result<OidcSigningKeyListResponse>> ActivateAsync(
        string keyId,
        OidcManagementActorContext actor,
        CancellationToken cancellationToken = default)
    {
        if (keyRing.UsesEphemeralDevelopmentKey || !keyRing.Entries.TryGetValue(keyId, out var key) || !key.HasPrivateKey)
        {
            return Result<OidcSigningKeyListResponse>.Failure(new Error(
                IdentityErrorCodes.OidcSigningKeyNotActivatable,
                "The signing key is unknown or does not include private material.", ErrorType.Validation));
        }
        var activated = await transaction.ExecuteAsync(async token =>
        {
            if (!await signingPolicy.ActivateAsync(keyId, token).ConfigureAwait(false)) return false;
            await auditWriter.WriteAsync(
                actor.ActorUserId,
                OidcManagementAuditWriter.SigningKeyActivatedEventType,
                $"key:{keyId}",
                actor.IpAddress,
                actor.UserAgent,
                token)
            .ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
        if (!activated) return Result<OidcSigningKeyListResponse>.Failure(new Error(
            IdentityErrorCodes.OidcSigningKeyNotActivatable, "The signing policy changed or key material is unavailable.", ErrorType.Conflict));
        await signingPolicy.SynchronizeAsync(cancellationToken).ConfigureAwait(false);
        return await queries.ListAsync(cancellationToken).ConfigureAwait(false);
    }
}
