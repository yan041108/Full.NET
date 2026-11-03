using Full.NET.Abstractions.Messaging;

namespace Full.NET.Modules.Identity.Oidc;

/// <summary>签发前同步材料权威；激活与保留窗口属于同一个 Identity 本地事务。</summary>
internal sealed class IdentityOidcSigningPolicy(IdentityOidcKeyGovernance governance, ICommandTransaction transaction)
{
    public Task SynchronizeAsync(CancellationToken cancellationToken) => governance.SynchronizeAsync(cancellationToken);
    public Task<bool> ActivateAsync(string keyId, CancellationToken cancellationToken) =>
        transaction.ExecuteAsync(ct => governance.ActivateAsync(keyId, ct), cancellationToken);
}
