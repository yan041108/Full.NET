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

internal sealed class OrganizationParentDetailQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions,
    IUserDataScopeResolver dataScopeResolver,
    IDataScopeSqlFilterBuilder dataScopeFilterBuilder)
{

            public async Task<Result<PagedResult<OrganizationParentDetailResponse>>> ListAsync(
                Guid currentUserId,
                bool isSuperAdministrator,
                int page,
                int pageSize,
                CancellationToken cancellationToken = default,
                OrganizationParentDetailListQuery? query = null)
            {
                    query ??= new();
                if (query.Validate() is { } queryError)
                    return Result<PagedResult<OrganizationParentDetailResponse>>.Failure(queryError);
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
                    DatabaseProvider.SqlServer => OrganizationParentDetailSql.QueryCountSqlServerStatement,
                    DatabaseProvider.MySql => OrganizationParentDetailSql.QueryCountMySqlStatement,
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
                        OrganizationParentDetailSql.ListSqlServerStatement,
                    DatabaseProvider.MySql =>
                        OrganizationParentDetailSql.ListMySqlStatement,
                    _ => throw new InvalidOperationException(
                        "The configured database provider is not supported."),
                };
                var listStatement = GeneratedTenantDataScopeComposer.ApplyDataScopeFilter(
                    baseStatement,
                    filter,
                    "WHERE 1 = 1\n            AND TenantId = @TenantId");
                var rows = await queryExecutor
                    .QueryAsync<OrganizationParentDetailRecord>(
                        listStatement,
                        GeneratedTenantDataScopeComposer.MergeParameters(
                            query.Parameters(offset, pageSize),
                            filter),
                        cancellationToken)
                    .ConfigureAwait(false);
                return Result<PagedResult<OrganizationParentDetailResponse>>.Success(
                    new PagedResult<OrganizationParentDetailResponse>(
                        rows.Select(Map).ToArray(),
                        page,
                        pageSize,
                        total));
            }

            public async Task<Result<OrganizationParentDetailResponse>> GetByIdAsync(
                Guid organizationParentDetailId,
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
                    OrganizationParentDetailSql.FindByIdStatement,
                    filter,
                    "AND TenantId = @TenantId");
                var record = await queryExecutor
                    .QuerySingleOrDefaultAsync<OrganizationParentDetailRecord>(
                        statement,
                        GeneratedTenantDataScopeComposer.MergeParameters(
                            new Dictionary<string, object?> { ["Id"] = organizationParentDetailId },
                            filter),
                        cancellationToken)
                    .ConfigureAwait(false);
                return record is null
                    ? NotFound()
                    : Result<OrganizationParentDetailResponse>.Success(Map(record));
            }

            internal async Task<Result<OrganizationParentDetailResponse>> FindByIdAsync(
                Guid organizationParentDetailId,
                CancellationToken cancellationToken = default)
            {
                var record = await queryExecutor
                    .QuerySingleOrDefaultAsync<OrganizationParentDetailRecord>(
                        OrganizationParentDetailSql.FindByIdStatement,
                        new Dictionary<string, object?> { ["Id"] = organizationParentDetailId },
                        cancellationToken)
                    .ConfigureAwait(false);
                return record is null
                    ? NotFound()
                    : Result<OrganizationParentDetailResponse>.Success(Map(record));
            }

    private static OrganizationParentDetailResponse Map(
        OrganizationParentDetailRecord record) =>
        new(
            record.Id,
            record.TenantId,
            record.OrganizationUnitId,
            record.Name,
            record.ParentReferenceId,
            record.Version,
            record.IsDeleted,
            record.DeletedAtUtc,
            record.DeletedById);

    private static Result<OrganizationParentDetailResponse> NotFound() =>
        OrganizationParentDetailFeatureErrors.NotFound();

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

internal sealed class OrganizationParentDetailManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    OrganizationParentDetailQueryService queries,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator,
    IOrganizationOwnedEntityWriteAuthorizer writeAuthorizer)
{
    private async Task<Result<OrganizationParentDetailResponse>> ExecuteWriteAsync(
        Func<CancellationToken, Task<Result<OrganizationParentDetailResponse>>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transaction.ExecuteResultAsync(action, cancellationToken).ConfigureAwait(false);
        }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            return Result<OrganizationParentDetailResponse>.Failure(new Error(
                "catalog.organization_childs.unique_conflict",
                "The unique field value already exists.", ErrorType.Conflict));
        }
    }
    public Task<Result<OrganizationParentDetailResponse>> CreateAsync(
        CreateOrganizationParentDetailRequest request,
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

    private async Task<Result<OrganizationParentDetailResponse>> CreateCoreAsync(
        CreateOrganizationParentDetailRequest request,
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
                        return Result<OrganizationParentDetailResponse>.Failure(authorization.Error!);
                    }        var validationError = ValidateWriteRequest(
            request.Name,
            request.ParentReferenceId);
        if (validationError is not null)
        {
            return validationError;
        }

        var organizationParentDetailId = idGenerator.NewId();

        var affectedRows = await commandExecutor.ExecuteAsync(
                OrganizationParentDetailSql.InsertStatement,
                new Dictionary<string, object?> { ["Id"] = organizationParentDetailId, ["OrganizationUnitId"] = organizationUnitId, ["Name"] = request.Name, ["ParentId"] = request.ParentReferenceId, ["Version"] = 1L, ["IsDeleted"] = false, ["DeletedAtUtc"] = (DateTimeOffset?)null, ["DeletedById"] = (Guid?)null },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                "The generated insert must affect exactly one row.");
        }

        return await queries.FindByIdAsync(
                organizationParentDetailId,
                cancellationToken)
            .ConfigureAwait(false);
    }


    public Task<Result<OrganizationParentDetailResponse>> UpdateAsync(
        Guid organizationParentDetailId,
        UpdateOrganizationParentDetailRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(
            token => UpdateCoreAsync(
                organizationParentDetailId,
                request,
                actorUserId,
                token),
            cancellationToken);

    private async Task<Result<OrganizationParentDetailResponse>> UpdateCoreAsync(
        Guid organizationParentDetailId,
        UpdateOrganizationParentDetailRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();

                var existingForAuthorization = await queries.FindByIdAsync(
                        organizationParentDetailId,
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
                    return Result<OrganizationParentDetailResponse>.Failure(authorization.Error!);
                }    var validationError = ValidateWriteRequest(
        request.Name,
        request.ParentReferenceId);
    if (validationError is not null)
    {
        return validationError;
    }

        var affectedRows = await commandExecutor.ExecuteAsync(
                OrganizationParentDetailSql.UpdateStatement,
                new Dictionary<string, object?> { ["Id"] = organizationParentDetailId, ["Name"] = request.Name, ["ParentId"] = request.ParentReferenceId, ["Version"] = request.Version },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveWriteFailureAsync(
                    organizationParentDetailId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return await queries.FindByIdAsync(
                organizationParentDetailId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<Result<OrganizationParentDetailResponse>> DeleteAsync(
        Guid organizationParentDetailId,
            DeleteOrganizationParentDetailRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(
            token => DeleteCoreAsync(
                organizationParentDetailId, request,
                actorUserId,
                token),
            cancellationToken);

    private async Task<Result<OrganizationParentDetailResponse>> DeleteCoreAsync(
        Guid organizationParentDetailId,
            DeleteOrganizationParentDetailRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var existing = await queries.FindByIdAsync(
                organizationParentDetailId,
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
                        return Result<OrganizationParentDetailResponse>.Failure(authorization.Error!);
                    }
                    var affectedRows = await commandExecutor.ExecuteAsync(
                OrganizationParentDetailSql.DeleteStatement,
                new Dictionary<string, object?> { ["Id"] = organizationParentDetailId, ["DeletedAtUtc"] = clock.UtcNow, ["DeletedById"] = actorUserId, ["Version"] = request.Version },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveWriteFailureAsync(
                    organizationParentDetailId,
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

    private static Result<OrganizationParentDetailResponse>? ValidateWriteRequest(
        string name,
        Guid parentReferenceId)
    {
        if (string.IsNullOrWhiteSpace(name) || name is { Length: > 64 })
        {
            return ValidationFailure("Name");
        }

        if (parentReferenceId == Guid.Empty)
        {
            return ValidationFailure("ParentReferenceId");
        }
        return null;
    }

    private static Result<OrganizationParentDetailResponse> ValidationFailure(
        string field) =>
        Result<OrganizationParentDetailResponse>.Failure(new Error(
            ValidationErrorCodes.Failed,
            "One or more generated field constraints were not satisfied.",
            ErrorType.Validation,
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [field] = ["The field value is invalid."],
            }, null, new[] { new ValidationViolation(field, "catalog.organization_childs.invalid_field", new Dictionary<string, object?>()) }));

    private async Task<Result<OrganizationParentDetailResponse>> ResolveWriteFailureAsync(
        Guid organizationParentDetailId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<OrganizationParentDetailRecord>(
                OrganizationParentDetailSql.FindByIdStatement,
                new Dictionary<string, object?> { ["Id"] = organizationParentDetailId },
                cancellationToken)
            .ConfigureAwait(false);
        return record is null ? NotFound() : VersionConflict();
    }

    private static Result<OrganizationParentDetailResponse> VersionConflict() =>
        Result<OrganizationParentDetailResponse>.Failure(new Error(
            OrganizationParentDetailErrorCodes.VersionConflict,
            "The resource was updated concurrently.",
            ErrorType.Conflict));

    private static Result<OrganizationParentDetailResponse> NotFound() =>
        OrganizationParentDetailFeatureErrors.NotFound();
}

internal static class OrganizationParentDetailFeatureErrors
{
    internal static Result<OrganizationParentDetailResponse> NotFound() =>
        Result<OrganizationParentDetailResponse>.Failure(new Error(
            OrganizationParentDetailErrorCodes.NotFound,
            "The resource was not found.",
            ErrorType.NotFound));
}
