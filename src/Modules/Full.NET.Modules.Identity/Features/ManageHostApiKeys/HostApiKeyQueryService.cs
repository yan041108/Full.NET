using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Features.ManageHostApiKeys;

/// <summary>Host API Key 分页列表只读查询。</summary>
internal sealed class HostApiKeyQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<HostApiKeyResponse>>> ListAsync(
        int page,
        int pageSize,
        Guid? userId,
        string? displayNameContains,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var filter = IdentitySqlParameters.Create(
            ("UserId", userId),
            ("DisplayNameContains", NormalizeFilter(displayNameContains)),
            ("Offset", offset),
            ("PageSize", pageSize));
        var (countStatement, listStatement) = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => (
                ApiKeySql.CountHostApiKeysSqlServer,
                ApiKeySql.ListHostApiKeysSqlServer),
            DatabaseProvider.MySql => (
                ApiKeySql.CountHostApiKeysMySql,
                ApiKeySql.ListHostApiKeysMySql),
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                countStatement,
                filter,
                cancellationToken)
            .ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<ApiKeyListRow>(
                listStatement,
                filter,
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows.Select(Map).ToArray();
        return Result<PagedResult<HostApiKeyResponse>>.Success(
            new PagedResult<HostApiKeyResponse>(items, page, pageSize, total));
    }

    private static HostApiKeyResponse Map(ApiKeyListRow row) =>
        new(
            row.Id,
            row.UserId,
            row.Username,
            row.DisplayName,
            row.KeyPrefix,
            Security.ApiKeyAuthenticationService.DeserializePermissions(row.PermissionsJson),
            row.ExpiresAtUtc,
            row.IsActive,
            row.LastUsedAtUtc,
            row.CreatedAtUtc);

    private static string? NormalizeFilter(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }
}
