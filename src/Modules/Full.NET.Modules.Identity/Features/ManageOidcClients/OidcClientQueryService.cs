using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Full.NET.Modules.Identity.Features.ManageOidcClients;

internal sealed class OidcClientQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<OidcClientResponse>> GetByIdAsync(
        Guid clientId,
        CancellationToken cancellationToken = default)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<IdentityOidcApplicationRow>(
                IdentityOidcSql.FindApplicationById,
                IdentitySqlParameters.Create(("Id", clientId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return NotFound();
        }

        return Result<OidcClientResponse>.Success(Map(row));
    }

    public async Task<Result<PagedResult<OidcClientResponse>>> ListAsync(
        int page,
        int pageSize,
        string? clientIdContains,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var filter = IdentitySqlParameters.Create(
            ("ClientIdContains", NormalizeFilter(clientIdContains)),
            ("Offset", offset),
            ("PageSize", pageSize));
        var (countStatement, listStatement) = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => (
                IdentityOidcSql.CountApplicationsFilteredSqlServer,
                IdentityOidcSql.ListApplicationsFilteredSqlServer),
            DatabaseProvider.MySql => (
                IdentityOidcSql.CountApplicationsFilteredMySql,
                IdentityOidcSql.ListApplicationsFilteredMySql),
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                countStatement,
                filter,
                cancellationToken)
            .ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<IdentityOidcApplicationRow>(
                listStatement,
                filter,
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows.Select(Map).ToArray();
        return Result<PagedResult<OidcClientResponse>>.Success(
            new PagedResult<OidcClientResponse>(items, page, pageSize, total));
    }

    internal static OidcClientResponse Map(IdentityOidcApplicationRow row)
    {
        var redirectUris = IdentityOidcStoreSupport.DeserializeStringArray(row.RedirectUrisJson).ToArray();
        var postLogoutRedirectUris = IdentityOidcStoreSupport
            .DeserializeStringArray(row.PostLogoutRedirectUrisJson)
            .ToArray();
        var permissions = IdentityOidcStoreSupport.DeserializeStringArray(row.PermissionsJson);
        var scopes = permissions
            .Where(permission => permission.StartsWith(Permissions.Prefixes.Scope, StringComparison.Ordinal))
            .Select(permission => permission[Permissions.Prefixes.Scope.Length..])
            .OrderBy(scope => scope, StringComparer.Ordinal)
            .ToArray();
        var metadata = IdentityOidcClientMetadata.Read(
            IdentityOidcStoreSupport.DeserializeProperties(row.PropertiesJson));
        return new OidcClientResponse(
            row.Id,
            row.ClientId ?? string.Empty,
            row.DisplayName ?? row.ClientId ?? string.Empty,
            row.ClientType ?? ClientTypes.Public,
            redirectUris,
            postLogoutRedirectUris,
            scopes,
            metadata.IsFirstParty,
            metadata.ResourceAudience,
            metadata.IsDisabled,
            row.CreatedAtUtc,
            (int)row.Version);
    }

    private static string? NormalizeFilter(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static Result<OidcClientResponse> NotFound() =>
        Result<OidcClientResponse>.Failure(new Error(
            IdentityErrorCodes.OidcClientNotFound,
            "The OIDC client was not found.",
            ErrorType.NotFound));
}