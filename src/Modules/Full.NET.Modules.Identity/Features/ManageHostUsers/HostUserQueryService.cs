using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Features.ManageHostUsers;

/// <summary>Host 用户分页列表与详情只读查询。</summary>
internal sealed class HostUserQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions,
    IUserFieldProjectionResolver projectionResolver)
{
    public async Task<Result<PagedResult<HostUserResponse>>> ListAsync(
        Guid actorUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                IdentitySql.CountHostUsers,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => IdentitySql.ListHostUsersSqlServer,
            DatabaseProvider.MySql => IdentitySql.ListHostUsersMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var rows = await queryExecutor.QueryAsync<HostUserListRow>(
                statement,
                new { Offset = offset, PageSize = pageSize },
                cancellationToken)
            .ConfigureAwait(false);
        var projection = await projectionResolver.ResolveAsync(
                actorUserId,
                tenantId: null,
                FieldProjectionResourceKeys.HostUsers,
                cancellationToken)
            .ConfigureAwait(false);
        var projectedFields = await LoadProjectedFieldsAsync(
                rows.Select(row => row.Id).ToArray(),
                projection,
                cancellationToken)
            .ConfigureAwait(false);
        var profiles = await LoadProfilesAsync(
                rows.Select(row => row.Id).ToArray(),
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows.Select(row => Map(
            row,
            projectedFields[row.Id],
            profiles.GetValueOrDefault(row.Id))).ToArray();
        return Result<PagedResult<HostUserResponse>>.Success(
            new PagedResult<HostUserResponse>(items, page, pageSize, total));
    }

    public async Task<Result<IReadOnlyList<HostUserResponse>>> ExportAsync(
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        const int exportLimit = 5000;
        var rows = await LoadRowsAsync(0, exportLimit, cancellationToken)
            .ConfigureAwait(false);
        var projection = await projectionResolver.ResolveAsync(
                actorUserId,
                tenantId: null,
                FieldProjectionResourceKeys.HostUsers,
                cancellationToken)
            .ConfigureAwait(false);
        var projectedFields = await LoadProjectedFieldsAsync(
                rows.Select(row => row.Id).ToArray(),
                projection,
                cancellationToken)
            .ConfigureAwait(false);
        var profiles = await LoadProfilesAsync(
                rows.Select(row => row.Id).ToArray(),
                cancellationToken)
            .ConfigureAwait(false);
        return Result<IReadOnlyList<HostUserResponse>>.Success(
            rows.Select(row => Map(
                row,
                projectedFields[row.Id],
                profiles.GetValueOrDefault(row.Id))).ToArray());
    }

    public async Task<Result<HostUserResponse>> GetByIdAsync(
        Guid actorUserId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<HostUserListRow>(
                IdentitySql.FindHostUserProjectionBaseById,
                new { UserId = userId },
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        var projection = await projectionResolver.ResolveAsync(
                actorUserId,
                tenantId: null,
                FieldProjectionResourceKeys.HostUsers,
                cancellationToken)
            .ConfigureAwait(false);
        var projectedFields = await LoadProjectedFieldsAsync(
                [userId],
                projection,
                cancellationToken)
            .ConfigureAwait(false);
        var profiles = await LoadProfilesAsync([userId], cancellationToken)
            .ConfigureAwait(false);
        return Result<HostUserResponse>.Success(Map(
            record,
            projectedFields[userId],
            profiles.GetValueOrDefault(userId)));
    }

    private static HostUserResponse Map(
        HostUserListRow row,
        HostUserProjectedFieldsResponse projectedFields,
        HostUserProfileResponse? profile = null) =>
        new(
            row.Id,
            row.Username,
            row.DisplayName,
            row.IsActive,
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.Version,
            projectedFields,
            profile);

    private async Task<IReadOnlyDictionary<Guid, HostUserProfileResponse?>> LoadProfilesAsync(
        IReadOnlyList<Guid> userIds,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, HostUserProfileResponse?>();
        }

        var records = await queryExecutor.QueryAsync<HostUserProfileRecord>(
                IdentitySql.ListHostUserProfilesByIds,
                new { UserIds = userIds },
                cancellationToken)
            .ConfigureAwait(false);
        var profileMap = records.ToDictionary(
            record => record.UserId,
            record => HostUserProfileMapper.ToResponse(record));
        return userIds.ToDictionary(
            userId => userId,
            userId => profileMap.GetValueOrDefault(userId));
    }

    private async Task<IReadOnlyDictionary<Guid, HostUserProjectedFieldsResponse>>
        LoadProjectedFieldsAsync(
            IReadOnlyList<Guid> userIds,
            UserFieldProjection projection,
            CancellationToken cancellationToken)
    {
        var effectiveKeys = projection.FieldKeys.ToHashSet(StringComparer.Ordinal);
        var locales = effectiveKeys.Contains("preferred_locale")
            ? await QueryValuesAsync<HostUserPreferredLocaleRow, string?>(
                IdentitySql.ListHostUserPreferredLocales,
                userIds,
                row => row.Id,
                row => row.Value,
                cancellationToken).ConfigureAwait(false)
            : new Dictionary<Guid, string?>();
        var failedCounts = effectiveKeys.Contains("failed_login_count")
            ? await QueryValuesAsync<HostUserFailedLoginCountRow, int?>(
                IdentitySql.ListHostUserFailedLoginCounts,
                userIds,
                row => row.Id,
                row => row.Value,
                cancellationToken).ConfigureAwait(false)
            : new Dictionary<Guid, int?>();
        var lockoutEnds = effectiveKeys.Contains("lockout_end_utc")
            ? await QueryValuesAsync<HostUserLockoutEndUtcRow, DateTimeOffset?>(
                IdentitySql.ListHostUserLockoutEnds,
                userIds,
                row => row.Id,
                row => row.Value,
                cancellationToken).ConfigureAwait(false)
            : new Dictionary<Guid, DateTimeOffset?>();

        return userIds.ToDictionary(
            userId => userId,
            userId => new HostUserProjectedFieldsResponse(
                projection.FieldKeys,
                locales.GetValueOrDefault(userId),
                failedCounts.GetValueOrDefault(userId),
                lockoutEnds.GetValueOrDefault(userId)));
    }

    private async Task<IReadOnlyDictionary<Guid, TValue>> QueryValuesAsync<TRow, TValue>(
        SqlStatement statement,
        IReadOnlyList<Guid> userIds,
        Func<TRow, Guid> idSelector,
        Func<TRow, TValue> valueSelector,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, TValue>();
        }

        var rows = await queryExecutor.QueryAsync<TRow>(
                statement,
                new { UserIds = userIds },
                cancellationToken)
            .ConfigureAwait(false);
        return rows.ToDictionary(idSelector, valueSelector);
    }

    private static Result<HostUserResponse> NotFound() =>
        Result<HostUserResponse>.Failure(new Error(
            IdentityErrorCodes.UserNotFound,
            "The host user was not found.",
            ErrorType.NotFound));

    private async Task<IReadOnlyList<HostUserListRow>> LoadRowsAsync(
        long offset,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => IdentitySql.ListHostUsersSqlServer,
            DatabaseProvider.MySql => IdentitySql.ListHostUsersMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        return await queryExecutor.QueryAsync<HostUserListRow>(
                statement,
                new { Offset = offset, PageSize = pageSize },
                cancellationToken)
            .ConfigureAwait(false);
    }
}
