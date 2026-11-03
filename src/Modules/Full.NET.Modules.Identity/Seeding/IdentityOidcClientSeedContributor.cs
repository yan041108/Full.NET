using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Seeding.Abstractions;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Seeding;

/// <summary>仅由 Migrator 受控初始化客户端；重跑不得覆盖管理员已保存的策略和密钥。</summary>
internal sealed class IdentityOidcClientSeedContributor(
    IOptions<IdentityOidcOptions> options,
    IOpenIddictApplicationManager applications,
    IOpenIddictScopeManager scopes,
    IQueryExecutor queries,
    ICommandExecutor commands,
    IIdGenerator ids,
    IClock clock,
    IOptions<IdentityBrowserOptions> browserOptions,
    IOptions<DatabaseOptions> databaseOptions,
    IOptions<IdentityOptions> identityOptions) : IDataSeedContributor
{
    public string Name => "identity.oidc_clients";
    public int Version => 3;
    public IReadOnlySet<SeedProfile> Profiles { get; } = new HashSet<SeedProfile> { SeedProfile.Baseline };
    public IReadOnlyCollection<string> Dependencies { get; } = [];

    public async Task<SeedContributionResult> SeedAsync(SeedContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        IdentityBrowserOptionsValidation.Validate(browserOptions.Value, options.Value);
        var previousActive = await queries.QuerySingleOrDefaultAsync<string>(IdentityOidcSigningPolicySql.FindActiveKey,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(options.Value.ActiveSigningKeyId) && previousActive is null)
        {
            await commands.ExecuteAsync(IdentityOidcSigningPolicySql.Insert,
                IdentitySqlParameters.Create(("Id", ids.NewId()), ("ActiveKeyId", options.Value.ActiveSigningKeyId),
                    ("Now", clock.UtcNow)), cancellationToken).ConfigureAwait(false);
        }
        if (options.Value.SigningKeys.Count > 0)
        {
            var known = await queries.QueryAsync<IdentityOidcSigningKeyRow>(IdentityOidcKeyGovernanceSql.Keys, cancellationToken: cancellationToken).ConfigureAwait(false);
            var active = previousActive ?? options.Value.ActiveSigningKeyId;
            var initialUpgrade = previousActive is not null && known.Count == 0;
            var historicalExpiry = initialUpgrade ? await queries.QuerySingleOrDefaultAsync<IdentityOidcSigningExpiryRow>(
                IdentityOidcKeyGovernanceSql.LatestExpiry, cancellationToken: cancellationToken).ConfigureAwait(false) : null;
            foreach (var configured in options.Value.SigningKeys)
            {
                if (!IdentityOidcKeyMaterial.IsValidId(configured.Key))
                    throw new InvalidOperationException("Invalid signing key identifier.");
                var hash = IdentityOidcKeyMaterial.Hash(configured.Value);
                var existing = known.SingleOrDefault(row => row.KeyId == configured.Key);
                if (existing is not null)
                {
                    if (existing.PublicKeyHash != hash) throw new InvalidOperationException("A signing key identifier cannot be rebound to different material.");
                    continue;
                }
                var everActive = configured.Key == active || initialUpgrade;
                // 首次补齐旧活动指针的历史不完整时，保守保留已部署材料一个完整令牌窗口。
                var retention = clock.UtcNow.AddDays(identityOptions.Value.RefreshTokenDays)
                    .AddMinutes(Math.Max(5, identityOptions.Value.AccessTokenMinutes));
                if (historicalExpiry?.ExpiryUtc > retention) retention = historicalExpiry.ExpiryUtc.Value.AddMinutes(5);
                await commands.ExecuteAsync(databaseOptions.Value.Provider == DatabaseProvider.MySql
                        ? IdentityOidcKeyGovernanceSql.InsertMySql : IdentityOidcKeyGovernanceSql.InsertSqlServer,
                    IdentitySqlParameters.Create(("Id", ids.NewId()), ("KeyId", configured.Key), ("PublicKeyHash", hash), ("Now", clock.UtcNow),
                        ("LastActivated", everActive ? clock.UtcNow : null), ("RetireAfter", configured.Key != active && everActive ? retention : null)), cancellationToken).ConfigureAwait(false);
            }
        }
        var clients = options.Value.Clients.ToList();
        if (browserOptions.Value.Enable)
        {
            var browser = browserOptions.Value;
            if (string.IsNullOrWhiteSpace(browser.ClientSecret)) throw new InvalidOperationException("Browser client secret is required.");
            clients.Add(new IdentityOidcClientOptions
            {
                ClientId = browser.ClientId, ClientSecret = browser.ClientSecret, IsFirstParty = true,
                RedirectUris = [browser.PublicOrigin + "/api/v1/identity/browser/callback"],
                PostLogoutRedirectUris = [browser.PublicOrigin + "/"], Scopes = ["openid", "profile", "offline_access"]
            });
        }
        var created = 0;
        foreach (var name in clients.SelectMany(client => client.Scopes).Distinct(StringComparer.Ordinal))
        {
            if (await scopes.FindByNameAsync(name, cancellationToken).ConfigureAwait(false) is null)
            {
                await scopes.CreateAsync(new OpenIddictScopeDescriptor { Name = name, DisplayName = name }, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        foreach (var client in clients)
        {
            if (await applications.FindByClientIdAsync(client.ClientId, cancellationToken).ConfigureAwait(false) is not null)
            {
                continue;
            }
            await applications.CreateAsync(IdentityOidcClientDescriptorFactory.BuildFromOptions(client), cancellationToken)
                .ConfigureAwait(false);
            created++;
        }
        return new SeedContributionResult(created, 0, clients.Count - created,
            created > 0 ? "seeding.data.created" : "seeding.data.skipped");
    }
}
