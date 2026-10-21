using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Features.ManageHostUsers;

/// <summary>Host 用户分页列表与详情只读查询。</summary>
internal sealed class HostUserQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions,
    IUserFieldProjectionResolver projectionResolver,
    IPermissionSnapshotReader permissionSnapshots,
    IActiveTenantContextResolver? tenantResolver = null)
{
    private const string HostScope = "host";
    public async Task<Result<PagedResult<HostUserResponse>>> ListAsync(
        Guid actorUserId,
        int page,
        int pageSize,
        bool includeProfile,
        CancellationToken cancellationToken = default,
        HostUserListFilter? filter = null)
    {
        filter ??= new();
        if (filter.OrganizationUnitId.HasValue != filter.OrganizationTenantId.HasValue
            || new[] { filter.Username, filter.DisplayName, filter.Phone, filter.Email }.Any(value => value?.Length > 128)
            || (filter.Status is not null and not "" and not "active" and not "inactive"))
            return Result<PagedResult<HostUserResponse>>.Failure(new Error(ValidationErrorCodes.Failed,
                "The user search filters are invalid.", ErrorType.Validation));
        var projection = await projectionResolver.ResolveAsync(actorUserId, null,
            FieldProjectionResourceKeys.HostUsers, cancellationToken).ConfigureAwait(false);
        if ((!string.IsNullOrWhiteSpace(filter.Phone) && !projection.FieldKeys.Contains("phone_number"))
            || (!string.IsNullOrWhiteSpace(filter.Email) && !projection.FieldKeys.Contains("email")))
            return Result<PagedResult<HostUserResponse>>.Failure(new Error(CommonErrorCodes.PermissionDenied,
                "The requested profile filter is not readable.", ErrorType.Forbidden));
        if (filter.OrganizationTenantId is { } tenantId && (tenantResolver is null
            || await tenantResolver.ResolveActiveByIdAsync(tenantId, cancellationToken).ConfigureAwait(false) is null))
            return Result<PagedResult<HostUserResponse>>.Failure(new Error(CommonErrorCodes.PermissionDenied,
                "The organization tenant is not available.", ErrorType.Forbidden));
        var revealPhone = !string.IsNullOrWhiteSpace(filter.Phone)
            && (await ResolveRevealAccessAsync(actorUserId, cancellationToken).ConfigureAwait(false)).CanRevealPhoneNumber;
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = ((long)page - 1) * pageSize;
        var parameters = IdentitySqlParameters.Create(("Offset", offset), ("PageSize", pageSize),
            ("Username", SqlSearchPattern.ContainsLiteral(filter.Username)),
            ("DisplayName", SqlSearchPattern.ContainsLiteral(filter.DisplayName)),
            ("Phone", SqlSearchPattern.ContainsLiteral(filter.Phone)), ("Email", SqlSearchPattern.ContainsLiteral(filter.Email)),
            ("RevealPhone", revealPhone), ("IsActive", filter.Status == "active" ? true : filter.Status == "inactive" ? false : (bool?)null),
            ("OrganizationTenantId", filter.OrganizationTenantId), ("OrganizationUnitId", filter.OrganizationUnitId));
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                databaseOptions.Value.Provider == DatabaseProvider.MySql ? HostUserSearchSql.CountMySql : HostUserSearchSql.CountSqlServer,
                parameters,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => HostUserSearchSql.ListSqlServer,
            DatabaseProvider.MySql => HostUserSearchSql.ListMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var rows = await queryExecutor.QueryAsync<HostUserListRow>(
                statement,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        var projectedFields = await LoadProjectedFieldsAsync(
                rows.Select(row => row.Id).ToArray(),
                projection,
                cancellationToken)
            .ConfigureAwait(false);
        var profiles = includeProfile
            ? await LoadProfilesAsync(
                    rows.Select(row => row.Id).ToArray(),
                    projection.FieldKeys,
                    actorUserId,
                    cancellationToken)
                .ConfigureAwait(false)
            : new Dictionary<Guid, HostUserProfileResponse?>();
        var items = rows.Select(row => Map(
            row,
            projectedFields[row.Id],
            profiles.GetValueOrDefault(row.Id))).ToArray();
        return Result<PagedResult<HostUserResponse>>.Success(
            new PagedResult<HostUserResponse>(items, page, pageSize, total));
    }

    public async Task<Result<IReadOnlyList<HostUserResponse>>> ExportAsync(
        Guid actorUserId,
        bool includeProfile,
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
        var profiles = includeProfile
            ? await LoadProfilesAsync(
                    rows.Select(row => row.Id).ToArray(),
                    projection.FieldKeys,
                    actorUserId,
                    cancellationToken)
                .ConfigureAwait(false)
            : new Dictionary<Guid, HostUserProfileResponse?>();
        return Result<IReadOnlyList<HostUserResponse>>.Success(
            rows.Select(row => Map(
                row,
                projectedFields[row.Id],
                profiles.GetValueOrDefault(row.Id))).ToArray());
    }

    public async Task<Result<HostUserResponse>> GetByIdAsync(
        Guid actorUserId,
        Guid userId,
        bool includeProfile,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<HostUserListRow>(
                IdentitySql.FindHostUserProjectionBaseById,
                IdentitySqlParameters.Create(("UserId", userId)),
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
        var profiles = includeProfile
            ? await LoadProfilesAsync([userId], projection.FieldKeys, actorUserId, cancellationToken)
                .ConfigureAwait(false)
            : new Dictionary<Guid, HostUserProfileResponse?>();
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
            row.AccountType,
            row.IsActive,
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.Version,
            projectedFields,
            profile,
            row.RetiredAtUtc);

    private async Task<IReadOnlyDictionary<Guid, HostUserProfileResponse?>> LoadProfilesAsync(
        IReadOnlyList<Guid> userIds,
        IReadOnlyCollection<string> effectiveFieldKeys,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0 || !HostUserProfileMapper.HasReadableFields(effectiveFieldKeys))
        {
            return new Dictionary<Guid, HostUserProfileResponse?>();
        }

        var columnMap = HostUserProfileMapper.GetReadableColumnMap(effectiveFieldKeys);
        if (columnMap.Count == 0)
        {
            return new Dictionary<Guid, HostUserProfileResponse?>();
        }

        var revealAccess = await ResolveRevealAccessAsync(actorUserId, cancellationToken)
            .ConfigureAwait(false);
        var statement = IdentitySql.BuildProjectedHostUserProfilesByIds(columnMap.Values.ToArray());
        var records = await queryExecutor.QueryAsync<HostUserProfileRecord>(
                statement,
                IdentitySqlParameters.Create(("UserIds", userIds)),
                cancellationToken)
            .ConfigureAwait(false);
        var profileMap = records.ToDictionary(
            record => record.UserId,
            record => HostUserProfileMapper.ToResponse(record, effectiveFieldKeys, revealAccess));
        return userIds.ToDictionary(
            userId => userId,
            userId => profileMap.GetValueOrDefault(userId));
    }

    private async Task<HostUserSensitiveFieldRevealAccess> ResolveRevealAccessAsync(
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var snapshot = await permissionSnapshots.ReadAsync(
                actorUserId,
                HostScope,
                tenantId: null,
                cancellationToken)
            .ConfigureAwait(false);
        return HostUserSensitiveFieldRevealAccess.FromPermissions(snapshot.Permissions);
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
                IdentitySqlParameters.Create(("UserIds", userIds)),
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
                IdentitySqlParameters.Create(("Offset", offset), ("PageSize", pageSize)),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
