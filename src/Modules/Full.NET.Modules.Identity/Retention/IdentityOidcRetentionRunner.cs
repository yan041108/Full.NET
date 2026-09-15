using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;

namespace Full.NET.Modules.Identity.Retention;

internal sealed record IdentityOidcRetentionResult(
    int TokensDeleted,
    int AuthorizationsDeleted)
{
    public static IdentityOidcRetentionResult Empty { get; } = new(0, 0);

    public int TotalDeleted => TokensDeleted + AuthorizationsDeleted;
}

/// <summary>
/// OIDC 授权与令牌保留清理执行器；仅删除超过保留期且已失效的孤儿授权与令牌记录。
/// </summary>
internal sealed class IdentityOidcRetentionRunner(
    ICommandExecutor commandExecutor,
    IClock clock,
    IOptions<IdentityOidcOptions> oidcOptions)
{
    public async Task<IdentityOidcRetentionResult> RunOnceAsync(
        IdentityOidcRetentionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled || !oidcOptions.Value.Enable)
        {
            return IdentityOidcRetentionResult.Empty;
        }

        var threshold = clock.UtcNow.AddDays(-options.RetentionDays);
        var tokensDeleted = await commandExecutor.ExecuteAsync(
                IdentityOidcSql.PruneTokens,
                IdentitySqlParameters.Create(
                    ("Threshold", threshold),
                    ("ValidStatus", OpenIddictConstants.Statuses.Valid)),
                cancellationToken)
            .ConfigureAwait(false);
        var authorizationsDeleted = await commandExecutor.ExecuteAsync(
                IdentityOidcSql.PruneAuthorizations,
                IdentitySqlParameters.Create(
                    ("Threshold", threshold),
                    ("ValidStatus", OpenIddictConstants.Statuses.Valid)),
                cancellationToken)
            .ConfigureAwait(false);

        return new IdentityOidcRetentionResult(tokensDeleted, authorizationsDeleted);
    }
}