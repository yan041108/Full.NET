using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Full.NET.Modules.Identity.Features.ManageOidcAuthorizations;

internal sealed class OidcAuthorizationQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<OidcAuthorizationResponse>> GetByIdAsync(
        Guid authorizationId,
        CancellationToken cancellationToken = default)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<IdentityOidcAuthorizationDetailRow>(
                IdentityOidcSql.FindAuthorizationDetailById,
                IdentitySqlParameters.Create(("Id", authorizationId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return NotFound();
        }

        return Result<OidcAuthorizationResponse>.Success(Map(row));
    }

    public async Task<Result<PagedResult<OidcAuthorizationResponse>>> ListAsync(
        int page,
        int pageSize,
        Guid? applicationId,
        string? subject,
        string? status,
        string? clientIdContains,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var filter = IdentitySqlParameters.Create(
            ("ApplicationId", applicationId),
            ("Subject", NormalizeFilter(subject)),
            ("Status", NormalizeFilter(status)),
            ("ClientIdContains", NormalizeFilter(clientIdContains)),
            ("Offset", offset),
            ("PageSize", pageSize));
        var (countStatement, listStatement) = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => (
                IdentityOidcSql.CountAuthorizationsFilteredSqlServer,
                IdentityOidcSql.ListAuthorizationsFilteredSqlServer),
            DatabaseProvider.MySql => (
                IdentityOidcSql.CountAuthorizationsFilteredMySql,
                IdentityOidcSql.ListAuthorizationsFilteredMySql),
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                countStatement,
                filter,
                cancellationToken)
            .ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<IdentityOidcAuthorizationDetailRow>(
                listStatement,
                filter,
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows.Select(Map).ToArray();
        return Result<PagedResult<OidcAuthorizationResponse>>.Success(
            new PagedResult<OidcAuthorizationResponse>(items, page, pageSize, total));
    }

    internal static OidcAuthorizationResponse Map(IdentityOidcAuthorizationDetailRow row)
    {
        var scopes = IdentityOidcStoreSupport.DeserializeStringArray(row.ScopesJson)
            .OrderBy(scope => scope, StringComparer.Ordinal)
            .ToArray();
        return new OidcAuthorizationResponse(
            row.Id,
            row.ApplicationId,
            row.ClientId,
            row.Subject,
            scopes,
            row.Status ?? string.Empty,
            row.Type ?? AuthorizationTypes.Permanent,
            row.CreationDateUtc,
            row.CreatedAtUtc,
            (int)row.Version);
    }

    internal static string? NormalizeFilter(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static Result<OidcAuthorizationResponse> NotFound() =>
        Result<OidcAuthorizationResponse>.Failure(new Error(
            IdentityErrorCodes.OidcAuthorizationNotFound,
            "The OIDC authorization was not found.",
            ErrorType.NotFound));
}