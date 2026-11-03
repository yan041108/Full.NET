#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;
using Full.NET.Modules.Organization.Contracts;
namespace Acme.Remediation.Generated;

internal sealed class OrganizationLinkQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions,
    IUserDataScopeResolver dataScopeResolver,
    IDataScopeSqlFilterBuilder dataScopeFilterBuilder)
{

            public async Task<Result<PagedResult<OrganizationLinkResponse>>> ListAsync(
                Guid currentUserId,
                bool isSuperAdministrator,
                int page,
                int pageSize,
                CancellationToken cancellationToken = default,
                OrganizationLinkListQuery? query = null)
            {
                    query ??= new();
                if (query.Validate() is { } queryError)
                    return Result<PagedResult<OrganizationLinkResponse>>.Failure(queryError);
                page = Math.Max(page, 1);
                pageSize = Math.Clamp(pageSize, 1, 100);
                var offset = (long)(page - 1) * pageSize;
                var scope = await dataScopeResolver.ResolveAsync(
                        currentUserId,
                        isSuperAdministrator,
                        cancellationToken)
                    .ConfigureAwait(false);
                var filter = dataScopeFilterBuilder.BuildOrganizationUnitFilter(
                    scope,
                    "OrganizationUnitId",
                    currentUserId);
                var countBase = databaseOptions.Value.Provider switch
                {
                    DatabaseProvider.SqlServer => OrganizationLinkSql.QueryCountSqlServerStatement,
                    DatabaseProvider.MySql => OrganizationLinkSql.QueryCountMySqlStatement,
                    _ => throw new InvalidOperationException("The configured database provider is not supported."),
                };
                var countStatement = GeneratedTenantDataScopeComposer.ApplyDataScopeFilter(
                    countBase,
                    filter,
                    "WHERE TenantId = @TenantId");
                var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                        countStatement,
                        GeneratedTenantDataScopeComposer.MergeParameters(query.Parameters(offset, pageSize), filter),
                        cancellationToken)
                    .ConfigureAwait(false);
                var baseStatement = databaseOptions.Value.Provider switch
                {
                    DatabaseProvider.SqlServer =>
                        OrganizationLinkSql.ListSqlServerStatement,
                    DatabaseProvider.MySql =>
                        OrganizationLinkSql.ListMySqlStatement,
                    _ => throw new InvalidOperationException(
                        "The configured database provider is not supported."),
                };
                var listStatement = GeneratedTenantDataScopeComposer.ApplyDataScopeFilter(
                    baseStatement,
                    filter,
                    "WHERE 1 = 1\n            AND TenantId = @TenantId");
                var rows = await queryExecutor
                    .QueryAsync<OrganizationLinkRecord>(
                        listStatement,
                        GeneratedTenantDataScopeComposer.MergeParameters(
                            query.Parameters(offset, pageSize),
                            filter),
                        cancellationToken)
                    .ConfigureAwait(false);
                return Result<PagedResult<OrganizationLinkResponse>>.Success(
                    new PagedResult<OrganizationLinkResponse>(
                        rows.Select(Map).ToArray(),
                        page,
                        pageSize,
                        total));
            }

            public async Task<Result<OrganizationLinkResponse>> GetByIdAsync(
                Guid organizationLinkId,
                Guid currentUserId,
                bool isSuperAdministrator,
                CancellationToken cancellationToken = default)
            {
                var scope = await dataScopeResolver.ResolveAsync(
                        currentUserId,
                        isSuperAdministrator,
                        cancellationToken)
                    .ConfigureAwait(false);
                var filter = dataScopeFilterBuilder.BuildOrganizationUnitFilter(
                    scope,
                    "OrganizationUnitId",
                    currentUserId);
                var statement = GeneratedTenantDataScopeComposer.ApplyDataScopeFilter(
                    OrganizationLinkSql.FindByIdStatement,
                    filter,
                    "AND TenantId = @TenantId");
                var record = await queryExecutor
                    .QuerySingleOrDefaultAsync<OrganizationLinkRecord>(
                        statement,
                        GeneratedTenantDataScopeComposer.MergeParameters(
                            new Dictionary<string, object?> { ["Id"] = organizationLinkId },
                            filter),
                        cancellationToken)
                    .ConfigureAwait(false);
                return record is null
                    ? NotFound()
                    : Result<OrganizationLinkResponse>.Success(Map(record));
            }

            internal async Task<Result<OrganizationLinkResponse>> FindByIdAsync(
                Guid organizationLinkId,
                CancellationToken cancellationToken = default)
            {
                var record = await queryExecutor
                    .QuerySingleOrDefaultAsync<OrganizationLinkRecord>(
                        OrganizationLinkSql.FindByIdStatement,
                        new Dictionary<string, object?> { ["Id"] = organizationLinkId },
                        cancellationToken)
                    .ConfigureAwait(false);
                return record is null
                    ? NotFound()
                    : Result<OrganizationLinkResponse>.Success(Map(record));
            }

    private static OrganizationLinkResponse Map(
        OrganizationLinkRecord record) =>
        new(
            record.Id,
            record.TenantId,
            record.OrganizationUnitId,
            record.SourceId,
            record.TargetId,
            record.Version,
            record.IsDeleted,
            record.DeletedAtUtc,
            record.DeletedById);

    private static Result<OrganizationLinkResponse> NotFound() =>
        OrganizationLinkFeatureErrors.NotFound();

    private static class GeneratedTenantDataScopeComposer
    {
        private const string CountTenantWhereAnchor =
            "WHERE 1 = 1";

        private const string ListTenantWhereAnchor =
            "WHERE 1 = 1";

        internal static SqlStatement ApplyDataScopeFilter(
            SqlStatement statement,
            DataScopeSqlFilter? filter,
            string tenantWhereAnchor)
        {
            if (filter is null)
            {
                return statement;
            }

            var text = InjectFilter(statement.Text, filter.Sql, tenantWhereAnchor);
            return statement with
            {
                Name = statement.Name + ".data_scope",
                Text = text,
            };
        }

        internal static object? MergeParameters(
            object? queryParameters,
            DataScopeSqlFilter? filter)
        {
            if (filter?.Parameters is null)
            {
                return queryParameters;
            }

            if (queryParameters is null)
            {
                return filter.Parameters;
            }

            var merged = new Dictionary<string, object?>(StringComparer.Ordinal);
            CopyProperties(queryParameters, merged);
            CopyProperties(filter.Parameters, merged);
            return merged;
        }

        private static string InjectFilter(
            string sql,
            string condition,
            string tenantWhereAnchor)
        {
            var index = sql.IndexOf(tenantWhereAnchor, StringComparison.Ordinal);
            if (index < 0)
            {
                throw new InvalidOperationException(
                    "Tenant-scoped SQL must contain the tenant boundary anchor.");
            }

            var insertAt = index + tenantWhereAnchor.Length;
            return sql.Insert(insertAt, $" AND ({condition})");
        }

        private static void CopyProperties(
            object source,
            IDictionary<string, object?> target)
        {
            if (source is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                foreach (var pair in pairs)
                {
                    target[pair.Key] = pair.Value;
                }

                return;
            }

            throw new InvalidOperationException("Data scope parameters must use a static dictionary.");
        }
    }
}

internal sealed class OrganizationLinkManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    OrganizationLinkQueryService queries,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator,
    IOrganizationOwnedEntityWriteAuthorizer writeAuthorizer)
{
    private async Task<Result<OrganizationLinkResponse>> ExecuteWriteAsync(
        Func<CancellationToken, Task<Result<OrganizationLinkResponse>>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transaction.ExecuteResultAsync(action, cancellationToken).ConfigureAwait(false);
        }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            return Result<OrganizationLinkResponse>.Failure(new Error(
                "catalog.organization_links.unique_conflict",
                "The unique field value already exists.", ErrorType.Conflict));
        }
    }
    public Task<Result<OrganizationLinkResponse>> CreateAsync(
        CreateOrganizationLinkRequest request,
Guid actorUserId,
Guid organizationUnitId,
CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(
            token => CreateCoreAsync(
                request,
                actorUserId,
                organizationUnitId,
                token),
            cancellationToken);

    private async Task<Result<OrganizationLinkResponse>> CreateCoreAsync(
        CreateOrganizationLinkRequest request,
Guid actorUserId,
Guid organizationUnitId,
CancellationToken cancellationToken = default)
    {
        EnsureTenantContext();

                    var authorization = await writeAuthorizer.EnsureCanWriteAsync(
                            currentTenant.Id!.Value,
                            organizationUnitId,
                            actorUserId,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (!authorization.IsSuccess)
                    {
                        return Result<OrganizationLinkResponse>.Failure(authorization.Error!);
                    }        var validationError = ValidateWriteRequest(
            request.SourceId,
            request.TargetId);
        if (validationError is not null)
        {
            return validationError;
        }

        var organizationLinkId = idGenerator.NewId();
        var removedPair = await queryExecutor.QuerySingleOrDefaultAsync<OrganizationLinkRecord>(
            new SqlStatement("catalog.organization_link.removed_pair", "SELECT Id, TenantId, OrganizationUnitId, SourceId, TargetId, Version, IsDeleted, DeletedAtUtc, DeletedById FROM acme_catalog_organization_link WHERE SourceId = @SourceId AND TargetId = @TargetId AND TenantId = @TenantId AND OrganizationUnitId = @OrganizationUnitId AND IsDeleted = 1", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId),
            new Dictionary<string, object?> { ["SourceId"] = request.SourceId, ["TargetId"] = request.TargetId, ["TenantId"] = currentTenant.Id!.Value, ["OrganizationUnitId"] = organizationUnitId }, cancellationToken).ConfigureAwait(false);
        if (removedPair is not null) organizationLinkId = removedPair.Id;

        var sourceIdPrincipalError = await EnsureSourceIdPrincipalExistsAsync(
            request.SourceId,
            organizationUnitId,
            cancellationToken)
            .ConfigureAwait(false);
        if (sourceIdPrincipalError is not null)
        {
            return sourceIdPrincipalError;
        }

        var targetIdPrincipalError = await EnsureTargetIdPrincipalExistsAsync(
            request.TargetId,
            organizationUnitId,
            cancellationToken)
            .ConfigureAwait(false);
        if (targetIdPrincipalError is not null)
        {
            return targetIdPrincipalError;
        }

        if (removedPair is not null)
        {
            var restored = await commandExecutor.ExecuteAsync(new SqlStatement("catalog.organization_link.restore_pair",
                "UPDATE acme_catalog_organization_link SET IsDeleted = 0, Version = Version + 1, DeletedAtUtc = NULL, DeletedById = NULL, SourceId = @SourceId, TargetId = @TargetId WHERE Id = @Id AND IsDeleted = 1 AND TenantId = @TenantId AND OrganizationUnitId = @OrganizationUnitId AND Version = @Version", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId),
                new Dictionary<string, object?> { ["Id"] = organizationLinkId, ["TenantId"] = currentTenant.Id!.Value, ["OrganizationUnitId"] = organizationUnitId, ["Version"] = removedPair.Version, ["SourceId"] = request.SourceId, ["TargetId"] = request.TargetId }, cancellationToken).ConfigureAwait(false);
            if (restored != 1) return OrganizationLinkFeatureErrors.UniqueConflict();
            return await queries.FindByIdAsync(organizationLinkId, cancellationToken).ConfigureAwait(false);
        }
        var affectedRows = await commandExecutor.ExecuteAsync(
                OrganizationLinkSql.InsertStatement,
                new Dictionary<string, object?> { ["Id"] = organizationLinkId, ["OrganizationUnitId"] = organizationUnitId, ["SourceId"] = request.SourceId, ["TargetId"] = request.TargetId, ["Version"] = 1L, ["IsDeleted"] = false, ["DeletedAtUtc"] = (DateTimeOffset?)null, ["DeletedById"] = (Guid?)null },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                "The generated insert must affect exactly one row.");
        }

        return await queries.FindByIdAsync(
                organizationLinkId,
                cancellationToken)
            .ConfigureAwait(false);
    }


    public Task<Result<OrganizationLinkResponse>> UpdateAsync(
        Guid organizationLinkId,
        UpdateOrganizationLinkRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(
            token => UpdateCoreAsync(
                organizationLinkId,
                request,
                actorUserId,
                token),
            cancellationToken);

    private async Task<Result<OrganizationLinkResponse>> UpdateCoreAsync(
        Guid organizationLinkId,
        UpdateOrganizationLinkRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();

                var existingForAuthorization = await queries.FindByIdAsync(
                        organizationLinkId,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!existingForAuthorization.IsSuccess)
                {
                    return existingForAuthorization;
                }

                var authorization = await writeAuthorizer.EnsureCanWriteAsync(
                        currentTenant.Id!.Value,
                        existingForAuthorization.Value!.OrganizationUnitId,
                        actorUserId,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!authorization.IsSuccess)
                {
                    return Result<OrganizationLinkResponse>.Failure(authorization.Error!);
                }    var validationError = ValidateWriteRequest(
        request.SourceId,
        request.TargetId);
    if (validationError is not null)
    {
        return validationError;
    }

        var sourceIdPrincipalError = await EnsureSourceIdPrincipalExistsAsync(
            request.SourceId,
            existingForAuthorization.Value!.OrganizationUnitId,
            cancellationToken)
            .ConfigureAwait(false);
        if (sourceIdPrincipalError is not null)
        {
            return sourceIdPrincipalError;
        }

        var targetIdPrincipalError = await EnsureTargetIdPrincipalExistsAsync(
            request.TargetId,
            existingForAuthorization.Value!.OrganizationUnitId,
            cancellationToken)
            .ConfigureAwait(false);
        if (targetIdPrincipalError is not null)
        {
            return targetIdPrincipalError;
        }

        var affectedRows = await commandExecutor.ExecuteAsync(
                OrganizationLinkSql.UpdateStatement,
                new Dictionary<string, object?> { ["Id"] = organizationLinkId, ["SourceId"] = request.SourceId, ["TargetId"] = request.TargetId, ["Version"] = request.Version },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveWriteFailureAsync(
                    organizationLinkId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return await queries.FindByIdAsync(
                organizationLinkId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<Result<OrganizationLinkResponse>> DeleteAsync(
        Guid organizationLinkId,
            DeleteOrganizationLinkRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(
            token => DeleteCoreAsync(
                organizationLinkId, request,
                actorUserId,
                token),
            cancellationToken);

    private async Task<Result<OrganizationLinkResponse>> DeleteCoreAsync(
        Guid organizationLinkId,
            DeleteOrganizationLinkRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var existing = await queries.FindByIdAsync(
                organizationLinkId,
                cancellationToken)
            .ConfigureAwait(false);
        if (!existing.IsSuccess)
        {
            return existing;
        }


                    var authorization = await writeAuthorizer.EnsureCanWriteAsync(
                            currentTenant.Id!.Value,
                            existing.Value!.OrganizationUnitId,
                            actorUserId,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (!authorization.IsSuccess)
                    {
                        return Result<OrganizationLinkResponse>.Failure(authorization.Error!);
                    }
                    var affectedRows = await commandExecutor.ExecuteAsync(
                OrganizationLinkSql.DeleteStatement,
                new Dictionary<string, object?> { ["Id"] = organizationLinkId, ["DeletedAtUtc"] = clock.UtcNow, ["DeletedById"] = actorUserId, ["Version"] = request.Version },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveWriteFailureAsync(
                    organizationLinkId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return existing;
    }
    private void EnsureTenantContext()
    {
        if (!currentTenant.IsAvailable
            || currentTenant.IsHost
            || currentTenant.Id is null)
        {
            throw new TenantContextMissingException(
                "catalog.tenant_context_required");
        }
    }

    private static Result<OrganizationLinkResponse>? ValidateWriteRequest(
        Guid sourceId,
        Guid targetId)
    {
        if (sourceId == Guid.Empty)
        {
            return ValidationFailure("SourceId");
        }

        if (targetId == Guid.Empty)
        {
            return ValidationFailure("TargetId");
        }
        return null;
    }

    private static Result<OrganizationLinkResponse> ValidationFailure(
        string field) =>
        Result<OrganizationLinkResponse>.Failure(new Error(
            ValidationErrorCodes.Failed,
            "One or more generated field constraints were not satisfied.",
            ErrorType.Validation,
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [field] = ["The field value is invalid."],
            }, null, new[] { new ValidationViolation(field, "catalog.organization_links.invalid_field", new Dictionary<string, object?>()) }));

    private async Task<Result<OrganizationLinkResponse>> ResolveWriteFailureAsync(
        Guid organizationLinkId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<OrganizationLinkRecord>(
                OrganizationLinkSql.FindByIdStatement,
                new Dictionary<string, object?> { ["Id"] = organizationLinkId },
                cancellationToken)
            .ConfigureAwait(false);
        return record is null ? NotFound() : VersionConflict();
    }

    private static Result<OrganizationLinkResponse> VersionConflict() =>
        Result<OrganizationLinkResponse>.Failure(new Error(
            OrganizationLinkErrorCodes.VersionConflict,
            "The resource was updated concurrently.",
            ErrorType.Conflict));
private async Task<Result<OrganizationLinkResponse>?> EnsureSourceIdPrincipalExistsAsync(
    Guid principalId,
    Guid organizationUnitId,
    CancellationToken cancellationToken)
{

    var found = await queryExecutor
        .QuerySingleOrDefaultAsync<Guid?>(
            new SqlStatement(
                "catalog.find_organization_source_for_organization_link_sourceId",
                "SELECT Id FROM acme_catalog_organization_source WHERE Id = @Id AND TenantId = @TenantId AND IsDeleted = 0 AND OrganizationUnitId = @OrganizationUnitId",
                SqlDataScope.TenantRequired,
                SqlTenantBinding.CurrentTenantId),
            new Dictionary<string, object?> { ["Id"] = principalId, ["TenantId"] = currentTenant.Id!.Value, ["OrganizationUnitId"] = organizationUnitId },
            cancellationToken)
        .ConfigureAwait(false);
    return found is null
        ? OrganizationLinkFeatureErrors.PrincipalNotFound()
        : null;
}

private async Task<Result<OrganizationLinkResponse>?> EnsureTargetIdPrincipalExistsAsync(
    Guid principalId,
    Guid organizationUnitId,
    CancellationToken cancellationToken)
{

    var found = await queryExecutor
        .QuerySingleOrDefaultAsync<Guid?>(
            new SqlStatement(
                "catalog.find_organization_target_for_organization_link_targetId",
                "SELECT Id FROM acme_catalog_organization_target WHERE Id = @Id AND TenantId = @TenantId AND IsDeleted = 0 AND OrganizationUnitId = @OrganizationUnitId",
                SqlDataScope.TenantRequired,
                SqlTenantBinding.CurrentTenantId),
            new Dictionary<string, object?> { ["Id"] = principalId, ["TenantId"] = currentTenant.Id!.Value, ["OrganizationUnitId"] = organizationUnitId },
            cancellationToken)
        .ConfigureAwait(false);
    return found is null
        ? OrganizationLinkFeatureErrors.PrincipalNotFound()
        : null;
}

    private static Result<OrganizationLinkResponse> NotFound() =>
        OrganizationLinkFeatureErrors.NotFound();
}

internal static class OrganizationLinkFeatureErrors
{
    internal static Result<OrganizationLinkResponse> NotFound() =>
        Result<OrganizationLinkResponse>.Failure(new Error(
            OrganizationLinkErrorCodes.NotFound,
            "The resource was not found.",
            ErrorType.NotFound));

internal static Result<OrganizationLinkResponse> UniqueConflict() =>
    Result<OrganizationLinkResponse>.Failure(new Error(
        OrganizationLinkErrorCodes.UniqueConflict,
        "The unique field value already exists.",
        ErrorType.Conflict));

internal static Result<OrganizationLinkResponse> PrincipalNotFound() =>
    Result<OrganizationLinkResponse>.Failure(new Error(
        OrganizationLinkErrorCodes.PrincipalNotFound,
        "The related principal was not found in the current module.",
        ErrorType.Validation));
}
