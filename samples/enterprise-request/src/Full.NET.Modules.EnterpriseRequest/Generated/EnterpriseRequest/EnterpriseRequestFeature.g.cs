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
namespace Full.NET.Modules.EnterpriseRequest.Generated;

internal sealed class EnterpriseRequestQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions,
    IUserDataScopeResolver dataScopeResolver,
    IDataScopeSqlFilterBuilder dataScopeFilterBuilder)
{

            public async Task<Result<PagedResult<EnterpriseRequestResponse>>> ListAsync(
                Guid currentUserId,
                bool isSuperAdministrator,
                int page,
                int pageSize,
                CancellationToken cancellationToken = default)
            {
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
                var countStatement = GeneratedTenantDataScopeComposer.ApplyDataScopeFilter(
                    EnterpriseRequestSql.CountStatement,
                    filter,
                    "WHERE TenantId = @TenantId");
                var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                        countStatement,
                        GeneratedTenantDataScopeComposer.MergeParameters(
                            null,
                            filter),
                        cancellationToken)
                    .ConfigureAwait(false);
                var baseStatement = databaseOptions.Value.Provider switch
                {
                    DatabaseProvider.SqlServer =>
                        EnterpriseRequestSql.ListSqlServerStatement,
                    DatabaseProvider.MySql =>
                        EnterpriseRequestSql.ListMySqlStatement,
                    _ => throw new InvalidOperationException(
                        "The configured database provider is not supported."),
                };
                var listStatement = GeneratedTenantDataScopeComposer.ApplyDataScopeFilter(
                    baseStatement,
                    filter,
                    "WHERE 1 = 1\n            AND TenantId = @TenantId");
                var rows = await queryExecutor
                    .QueryAsync<EnterpriseRequestRecord>(
                        listStatement,
                        GeneratedTenantDataScopeComposer.MergeParameters(
                            new Dictionary<string, object?> { ["Offset"] = offset, ["PageSize"] = pageSize },
                            filter),
                        cancellationToken)
                    .ConfigureAwait(false);
                return Result<PagedResult<EnterpriseRequestResponse>>.Success(
                    new PagedResult<EnterpriseRequestResponse>(
                        rows.Select(Map).ToArray(),
                        page,
                        pageSize,
                        total));
            }

            public async Task<Result<EnterpriseRequestResponse>> GetByIdAsync(
                Guid enterpriseRequestId,
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
                    EnterpriseRequestSql.FindByIdStatement,
                    filter,
                    "AND TenantId = @TenantId");
                var record = await queryExecutor
                    .QuerySingleOrDefaultAsync<EnterpriseRequestRecord>(
                        statement,
                        GeneratedTenantDataScopeComposer.MergeParameters(
                            new Dictionary<string, object?> { ["Id"] = enterpriseRequestId },
                            filter),
                        cancellationToken)
                    .ConfigureAwait(false);
                return record is null
                    ? NotFound()
                    : Result<EnterpriseRequestResponse>.Success(Map(record));
            }

            internal async Task<Result<EnterpriseRequestResponse>> FindByIdAsync(
                Guid enterpriseRequestId,
                CancellationToken cancellationToken = default)
            {
                var record = await queryExecutor
                    .QuerySingleOrDefaultAsync<EnterpriseRequestRecord>(
                        EnterpriseRequestSql.FindByIdStatement,
                        new Dictionary<string, object?> { ["Id"] = enterpriseRequestId },
                        cancellationToken)
                    .ConfigureAwait(false);
                return record is null
                    ? NotFound()
                    : Result<EnterpriseRequestResponse>.Success(Map(record));
            }

    private static EnterpriseRequestResponse Map(
        EnterpriseRequestRecord record) =>
        new(
            record.Id,
            record.TenantId,
            record.OrganizationUnitId,
            record.RequestNumber,
            record.Title,
            record.Status,
            record.TotalAmount,
            record.ApplicantUserId,
            record.Version,
            record.CreatedAtUtc,
            record.CreatedById,
            record.UpdatedAtUtc,
            record.UpdatedById,
            record.IsDeleted,
            record.DeletedAtUtc,
            record.DeletedById);

    private static Result<EnterpriseRequestResponse> NotFound() =>
        EnterpriseRequestFeatureErrors.NotFound();

    private static class GeneratedTenantDataScopeComposer
    {
        private const string CountTenantWhereAnchor =
            "WHERE TenantId = @TenantId";

        private const string ListTenantWhereAnchor =
            "WHERE 1 = 1\n            AND TenantId = @TenantId";

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

internal sealed class EnterpriseRequestManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    EnterpriseRequestQueryService queries,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator,
    IOrganizationOwnedEntityWriteAuthorizer writeAuthorizer)
{
    public Task<Result<EnterpriseRequestResponse>> CreateAsync(
        CreateEnterpriseRequestRequest request,
Guid actorUserId,
Guid organizationUnitId,
CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => CreateCoreAsync(
                request,
                actorUserId,
                organizationUnitId,
                token),
            cancellationToken);

    private async Task<Result<EnterpriseRequestResponse>> CreateCoreAsync(
        CreateEnterpriseRequestRequest request,
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
                        return Result<EnterpriseRequestResponse>.Failure(authorization.Error!);
                    }        var validationError = ValidateWriteRequest(
            request.RequestNumber,
            request.Title,
            request.Status);
        if (validationError is not null)
        {
            return validationError;
        }

        var enterpriseRequestId = idGenerator.NewId();
        var affectedRows = await commandExecutor.ExecuteAsync(
                EnterpriseRequestSql.InsertStatement,
                new
                {
                    Id = enterpriseRequestId,
                    OrganizationUnitId = organizationUnitId,
                    request.RequestNumber,
                    request.Title,
                    request.Status,
                    request.TotalAmount,
                    request.ApplicantUserId,
                    Version = 1L,
                    CreatedAtUtc = clock.UtcNow,
                    CreatedById = actorUserId,
                    UpdatedAtUtc = (DateTimeOffset?)null,
                    UpdatedById = (Guid?)null,
                    IsDeleted = false,
                    DeletedAtUtc = (DateTimeOffset?)null,
                    DeletedById = (Guid?)null
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                "The generated insert must affect exactly one row.");
        }

        return await queries.FindByIdAsync(
                enterpriseRequestId,
                cancellationToken)
            .ConfigureAwait(false);
    }


    public Task<Result<EnterpriseRequestResponse>> UpdateAsync(
        Guid enterpriseRequestId,
        UpdateEnterpriseRequestRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => UpdateCoreAsync(
                enterpriseRequestId,
                request,
                actorUserId,
                token),
            cancellationToken);

    private async Task<Result<EnterpriseRequestResponse>> UpdateCoreAsync(
        Guid enterpriseRequestId,
        UpdateEnterpriseRequestRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();

                var existingForAuthorization = await queries.FindByIdAsync(
                        enterpriseRequestId,
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
                    return Result<EnterpriseRequestResponse>.Failure(authorization.Error!);
                }    var validationError = ValidateWriteRequest(
        request.RequestNumber,
        request.Title,
        request.Status);
    if (validationError is not null)
    {
        return validationError;
    }

        var affectedRows = await commandExecutor.ExecuteAsync(
                EnterpriseRequestSql.UpdateStatement,
                new
                {
                    Id = enterpriseRequestId,
                    request.RequestNumber,
                    request.Title,
                    request.Status,
                    request.TotalAmount,
                    request.ApplicantUserId,
                    UpdatedAtUtc = clock.UtcNow,
                    UpdatedById = actorUserId,
                    request.Version
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveWriteFailureAsync(
                    enterpriseRequestId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return await queries.FindByIdAsync(
                enterpriseRequestId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<Result<EnterpriseRequestResponse>> DeleteAsync(
        Guid enterpriseRequestId,
            DeleteEnterpriseRequestRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => DeleteCoreAsync(
                enterpriseRequestId, request,
                actorUserId,
                token),
            cancellationToken);

    private async Task<Result<EnterpriseRequestResponse>> DeleteCoreAsync(
        Guid enterpriseRequestId,
            DeleteEnterpriseRequestRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var existing = await queries.FindByIdAsync(
                enterpriseRequestId,
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
                        return Result<EnterpriseRequestResponse>.Failure(authorization.Error!);
                    }
            await CascadeDeleteDependentsAsync(
            enterpriseRequestId,
            cancellationToken)
            .ConfigureAwait(false);
                var affectedRows = await commandExecutor.ExecuteAsync(
                EnterpriseRequestSql.DeleteStatement,
                new
                {
                    Id = enterpriseRequestId,
                    DeletedAtUtc = clock.UtcNow,
                    DeletedById = actorUserId,
                    request.Version
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveWriteFailureAsync(
                    enterpriseRequestId,
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
                "enterprise_request.tenant_context_required");
        }
    }

    private static Result<EnterpriseRequestResponse>? ValidateWriteRequest(
        string? requestNumber,
        string? title,
        string? status)
    {
        if (requestNumber is null || requestNumber.Length > 64)
        {
            return ValidationFailure("RequestNumber");
        }

        if (title is null || title.Length > 200)
        {
            return ValidationFailure("Title");
        }

        if (status is null || status.Length > 32)
        {
            return ValidationFailure("Status");
        }
        return null;
    }

    private static Result<EnterpriseRequestResponse> ValidationFailure(
        string field) =>
        Result<EnterpriseRequestResponse>.Failure(new Error(
            ValidationErrorCodes.Failed,
            "One or more generated field constraints were not satisfied.",
            ErrorType.Validation,
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [field] = ["The field value is invalid."],
            }));

    private async Task<Result<EnterpriseRequestResponse>> ResolveWriteFailureAsync(
        Guid enterpriseRequestId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<EnterpriseRequestRecord>(
                EnterpriseRequestSql.FindByIdStatement,
                new Dictionary<string, object?> { ["Id"] = enterpriseRequestId },
                cancellationToken)
            .ConfigureAwait(false);
        return record is null ? NotFound() : VersionConflict();
    }

    private static Result<EnterpriseRequestResponse> VersionConflict() =>
        Result<EnterpriseRequestResponse>.Failure(new Error(
            EnterpriseRequestErrorCodes.VersionConflict,
            "The resource was updated concurrently.",
            ErrorType.Conflict));
                private async Task CascadeDeleteDependentsAsync(
                    Guid enterpriseRequestId,
                    CancellationToken cancellationToken)
                {
        await commandExecutor.ExecuteAsync(
                new SqlStatement(
                    "enterprise_request.cascade_delete_enterprise_request_line",
                    "DELETE FROM demo_enterprise_request_enterprise_request_line WHERE RequestId = @Id AND TenantId = @TenantId",
                    SqlDataScope.TenantRequired,
                    SqlTenantBinding.CurrentTenantId),
                new
                {
                    Id = enterpriseRequestId,
                                    TenantId = currentTenant.Id!.Value,
                },
                cancellationToken)
            .ConfigureAwait(false);
                }

    private static Result<EnterpriseRequestResponse> NotFound() =>
        EnterpriseRequestFeatureErrors.NotFound();
}

internal static class EnterpriseRequestFeatureErrors
{
    internal static Result<EnterpriseRequestResponse> NotFound() =>
        Result<EnterpriseRequestResponse>.Failure(new Error(
            EnterpriseRequestErrorCodes.NotFound,
            "The resource was not found.",
            ErrorType.NotFound));
}
