using System.Collections.Immutable;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Oidc;

/// <summary>进程租约使用独立 UUID，重启后的进程不能继承旧进程就绪声明。</summary>
internal sealed class IdentityOidcSigningNodeIdentity(string role)
{
    public Guid Id { get; } = Guid.CreateVersion7();
    public string Role { get; } = role;
}

/// <summary>公开材料权威、预发布/保留/退役与节点就绪门禁；治理写入由调用方事务包围。</summary>
internal sealed class IdentityOidcKeyGovernance(IQueryExecutor queries, ICommandExecutor commands,
    ICommandTransaction transaction, IClock clock, IdentityOidcSigningKeyRing keys,
    IdentityOidcSigningNodeIdentity node, IOptions<IdentityOidcOptions> options,
    IOptions<IdentityOptions> identity, IOptions<DatabaseOptions> database, ICurrentTenantContextWriter tenant,
    IdentityOidcOpenIddictSigningCredentialSynchronizer? synchronizer = null)
{
    private bool MySql => database.Value.Provider == DatabaseProvider.MySql;
    internal TimeSpan RetentionWindow => TimeSpan.FromDays(identity.Value.RefreshTokenDays)
        + TimeSpan.FromMinutes(Math.Max(5, identity.Value.AccessTokenMinutes));

    internal Task<IReadOnlyList<IdentityOidcSigningKeyRow>> ReadKeysAsync(CancellationToken token) =>
        queries.QueryAsync<IdentityOidcSigningKeyRow>(IdentityOidcKeyGovernanceSql.Keys, cancellationToken: token);

    public async Task<bool> IsValidationKeyAllowedAsync(string keyId, CancellationToken token)
    {
        if (keys.UsesEphemeralDevelopmentKey) return keys.Entries.ContainsKey(keyId);
        if (!keys.Entries.TryGetValue(keyId, out var key)) return false;
        var wasHost = tenant.IsHost;
        var previous = tenant.Id is Guid id ? new TenantContext(id, tenant.Identifier!, tenant.Name!) : null;
        tenant.SetHost();
        try
        {
            // 退役是强一致安全状态，不能依赖进程公钥集合或就绪探针的刷新周期。
            return await queries.QuerySingleOrDefaultAsync<int>(IdentityOidcKeyGovernanceSql.ValidateKey,
                IdentitySqlParameters.Create(("KeyId", keyId), ("PublicKeyHash", IdentityOidcKeyMaterial.Hash(key.SecurityKey))), token).ConfigureAwait(false) == 1;
        }
        finally { if (previous is not null) tenant.SetTenant(previous); else if (wasHost) tenant.SetHost(); else tenant.Clear(); }
    }

    public async Task SynchronizeAsync(CancellationToken token)
    {
        if (keys.UsesEphemeralDevelopmentKey) return;
        var active = await ActiveAsync(token).ConfigureAwait(false);
        var rows = await ReadKeysAsync(token).ConfigureAwait(false);
        var entries = keys.Entries;
        var record = rows.SingleOrDefault(row => row.KeyId == active);
        if (record is null || record.IsRetired || !entries.TryGetValue(active, out var current)
            || !current.HasPrivateKey) throw new InvalidOperationException("Active signing governance material is unavailable.");
        foreach (var row in rows.Where(row => !row.IsRetired))
        {
            if (!entries.TryGetValue(row.KeyId, out var local))
            {
                if (row.LastActivatedAtUtc.HasValue) throw new InvalidOperationException("Retained public signing material is missing.");
                continue;
            }
            if (IdentityOidcKeyMaterial.Hash(local.SecurityKey) != row.PublicKeyHash)
                throw new InvalidOperationException("A signing key identifier was reused with different material.");
        }
        var changed = keys.ActiveSigningKeyId != active;
        if (!keys.TryActivate(active).IsSuccess) throw new InvalidOperationException("The active signing key is unavailable.");
        changed |= keys.ApplyRetired(rows.Where(row => row.IsRetired).Select(row => row.KeyId));
        if (changed) synchronizer?.Sync();
        keys.MarkGovernanceLoaded();
    }

    public async Task ReportNodeAsync(CancellationToken token)
    {
        if (keys.UsesEphemeralDevelopmentKey) return;
        var before = await queries.QuerySingleOrDefaultAsync<int>(IdentityOidcSigningPolicySql.FindVersion, cancellationToken: token).ConfigureAwait(false);
        await SynchronizeAsync(token).ConfigureAwait(false);
        var version = await queries.QuerySingleOrDefaultAsync<int>(IdentityOidcSigningPolicySql.FindVersion, cancellationToken: token).ConfigureAwait(false);
        if (before != version) throw new InvalidOperationException("Signing policy changed while preparing node readiness.");
        var parameters = IdentitySqlParameters.Create(("Id", node.Id), ("RoleKey", node.Role),
            ("Manifest", IdentityOidcKeyMaterial.Manifest(keys)), ("Encryption", ConfigurationFingerprint()),
            ("PrivateKeys", IdentityOidcStoreSupport.SerializeStringArray(keys.Entries.Where(pair => pair.Value.HasPrivateKey).Select(pair => pair.Key).Order(StringComparer.Ordinal).ToImmutableArray())),
            ("PolicyVersion", version), ("Expires", clock.UtcNow.AddSeconds(90)), ("Now", clock.UtcNow));
        await transaction.ExecuteAsync(async ct =>
        {
            await commands.ExecuteAsync(MySql ? IdentityOidcKeyGovernanceSql.InsertNodeMySql : IdentityOidcKeyGovernanceSql.InsertNodeSqlServer, parameters, ct).ConfigureAwait(false);
            await commands.ExecuteAsync(IdentityOidcKeyGovernanceSql.UpdateNode, parameters, ct).ConfigureAwait(false);
            await commands.ExecuteAsync(MySql ? IdentityOidcKeyGovernanceSql.PruneNodesMySql : IdentityOidcKeyGovernanceSql.PruneNodesSqlServer,
                IdentitySqlParameters.Create(("Before", clock.UtcNow.AddDays(-1))), ct).ConfigureAwait(false);
            return true;
        }, token).ConfigureAwait(false);
    }

    internal async Task<(int Version, Dictionary<string, bool> Keys)> ActivationReadinessAsync(CancellationToken token)
    {
        if (keys.UsesEphemeralDevelopmentKey) return (0, new(StringComparer.Ordinal));
        var rows = await ReadKeysAsync(token).ConfigureAwait(false);
        var nodes = await queries.QueryAsync<IdentityOidcSigningNodeRow>(MySql ? IdentityOidcKeyGovernanceSql.NodesMySql : IdentityOidcKeyGovernanceSql.NodesSqlServer,
            IdentitySqlParameters.Create(("Now", clock.UtcNow)), token).ConfigureAwait(false);
        var version = await queries.QuerySingleOrDefaultAsync<int>(IdentityOidcSigningPolicySql.FindVersion, cancellationToken: token).ConfigureAwait(false);
        var manifest = IdentityOidcKeyMaterial.Manifest(keys);
        var encryption = ConfigurationFingerprint();
        return (version, rows.ToDictionary(row => row.KeyId, row => !row.IsRetired
            && row.PublishedAtUtc.AddSeconds(options.Value.SigningPrepublicationSeconds) <= clock.UtcNow
            && keys.Entries.TryGetValue(row.KeyId, out var key) && key.HasPrivateKey && IdentityOidcKeyMaterial.Hash(key.SecurityKey) == row.PublicKeyHash
            && nodes.Count < 256 && nodes.Count(value => value.RoleKey == "api") >= options.Value.MinimumReadyApiNodes
            && nodes.All(value => value.SigningManifest == manifest && value.EncryptionFingerprint == encryption
                && value.PolicyVersion == version && IdentityOidcStoreSupport.DeserializeStringArray(value.PrivateKeysJson).Contains(row.KeyId, StringComparer.Ordinal)), StringComparer.Ordinal));
    }

    public async Task<bool> PublishAsync(string keyId, CancellationToken token)
    {
        if (keys.UsesEphemeralDevelopmentKey || !keys.Entries.TryGetValue(keyId, out var key)) return false;
        var existing = (await ReadKeysAsync(token).ConfigureAwait(false)).SingleOrDefault(row => row.KeyId == keyId);
        if (existing is not null) return !existing.IsRetired && existing.PublicKeyHash == IdentityOidcKeyMaterial.Hash(key.SecurityKey);
        await commands.ExecuteAsync(MySql ? IdentityOidcKeyGovernanceSql.InsertMySql : IdentityOidcKeyGovernanceSql.InsertSqlServer,
            IdentitySqlParameters.Create(("Id", Guid.CreateVersion7()), ("KeyId", keyId), ("PublicKeyHash", IdentityOidcKeyMaterial.Hash(key.SecurityKey)),
                ("Now", clock.UtcNow), ("LastActivated", null), ("RetireAfter", null)), token).ConfigureAwait(false);
        existing = (await ReadKeysAsync(token).ConfigureAwait(false)).Single(row => row.KeyId == keyId);
        return !existing.IsRetired && existing.PublicKeyHash == IdentityOidcKeyMaterial.Hash(key.SecurityKey);
    }

    public async Task<bool> ActivateAsync(string keyId, CancellationToken token)
    {
        var readiness = await ActivationReadinessAsync(token).ConfigureAwait(false);
        if (!readiness.Keys.GetValueOrDefault(keyId)) return false;
        var previous = await ActiveAsync(token).ConfigureAwait(false);
        if (previous == keyId) return true;
        var latest = await queries.QuerySingleOrDefaultAsync<IdentityOidcSigningExpiryRow>(IdentityOidcKeyGovernanceSql.LatestExpiry, cancellationToken: token).ConfigureAwait(false);
        var deadline = clock.UtcNow + RetentionWindow;
        if (latest?.ExpiryUtc > deadline) deadline = latest.ExpiryUtc.Value.AddMinutes(5);
        var count = await commands.ExecuteAsync(IdentityOidcSigningPolicySql.Activate,
            IdentitySqlParameters.Create(("ActiveKeyId", keyId), ("PreviousKeyId", previous), ("Version", readiness.Version), ("Now", clock.UtcNow)), token).ConfigureAwait(false);
        if (count != 1) return false;
        await commands.ExecuteAsync(IdentityOidcKeyGovernanceSql.Retain,
            IdentitySqlParameters.Create(("KeyId", previous), ("RetireAfter", deadline)), token).ConfigureAwait(false);
        await commands.ExecuteAsync(IdentityOidcKeyGovernanceSql.Activated,
            IdentitySqlParameters.Create(("KeyId", keyId), ("Now", clock.UtcNow)), token).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> RetireAsync(string keyId, CancellationToken token)
    {
        if (keys.UsesEphemeralDevelopmentKey) return false;
        var row = (await ReadKeysAsync(token).ConfigureAwait(false)).SingleOrDefault(value => value.KeyId == keyId);
        if (row is null || row.IsRetired || row.LastActivatedAtUtc.HasValue && (row.RetireAfterUtc is null || row.RetireAfterUtc > clock.UtcNow)) return false;
        // 与激活采用相同的策略行优先锁序，阻断“重新激活之后又退役”的竞争。
        if (await commands.ExecuteAsync(IdentityOidcKeyGovernanceSql.LockRetirementPolicy,
            IdentitySqlParameters.Create(("KeyId", keyId), ("Now", clock.UtcNow)), token).ConfigureAwait(false) != 1) return false;
        return await commands.ExecuteAsync(IdentityOidcKeyGovernanceSql.Retire,
            IdentitySqlParameters.Create(("KeyId", keyId), ("Version", row.Version), ("Now", clock.UtcNow)), token).ConfigureAwait(false) == 1;
    }

    private async Task<string> ActiveAsync(CancellationToken token) => await queries.QuerySingleOrDefaultAsync<string>(
        IdentityOidcSigningPolicySql.FindActiveKey, cancellationToken: token).ConfigureAwait(false)
        ?? throw new InvalidOperationException("Signing policy has not been initialized by Migrator.");

    private string ConfigurationFingerprint() => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(IdentityOidcKeyMaterial.Encryption(options.Value.EncryptionKeyBase64)
            + ":" + identity.Value.RefreshTokenDays.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ":" + identity.Value.AccessTokenMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture))));
}
