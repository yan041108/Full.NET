using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>复用受控生命周期场景，并额外验证仍缓存旧材料的存活对端必须拒绝退役密钥。</summary>
internal static class IdentityOidcMultiInstanceSigningKeyRetirementAssertions
{
    public static Task VerifyAsync(DatabaseProvider provider, string connectionString, CancellationToken cancellationToken = default) =>
        IdentityOidcSigningKeyRetirementAssertions.VerifyAsync(provider, connectionString, cancellationToken, checkStalePeer: true);
}
