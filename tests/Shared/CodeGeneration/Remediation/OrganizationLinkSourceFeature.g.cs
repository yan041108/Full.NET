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

internal sealed partial class OrganizationLinkSourceQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions,
    IUserDataScopeResolver dataScopeResolver,
    IDataScopeSqlFilterBuilder dataScopeFilterBuilder)
{

            public async Task<Result<PagedResult<OrganizationLinkSourceResponse>>> ListAsync(
                Guid currentUserId,
                bool isSuperAdministrator,
                int page,
                int pageSize,
                CancellationToken cancellationToken = default,
                OrganizationLinkSourceListQuery? query = null)
            {
                    query ??= new();
                if (query.Validate() is { } queryError)
                    return Result<PagedResult<OrganizationLinkSourceResponse>>.Failure(queryError);
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
                    DatabaseProvider.SqlServer => OrganizationLinkSourceSql.QueryCountSqlServerStatement,
                    DatabaseProvider.MySql => OrganizationLinkSourceSql.QueryCountMySqlStatement,
                    _ => throw new InvalidOperationException("The configured database provider is not supported."),
                };
                // 计数与分页共用固定租户条件追加范围，不依赖 WHERE 前导项或排版。
                ConfigureReadDataScope(scope, currentUserId, ref filter);
                var countStatement = GeneratedTenantDataScopeComposer.ApplyDataScopeFilter(
                    countBase,
                    filter,
                    "AND TenantId = @TenantId");
                var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                        countStatement,
                        GeneratedTenantDataScopeComposer.MergeParameters(query.Parameters(offset, pageSize), filter),
                        cancellationToken)
                    .ConfigureAwait(false);
                var baseStatement = databaseOptions.Value.Provider switch
                {
                    DatabaseProvider.SqlServer =>
                        OrganizationLinkSourceSql.ListSqlServerStatement,
                    DatabaseProvider.MySql =>
                        OrganizationLinkSourceSql.ListMySqlStatement,
                    _ => throw new InvalidOperationException(
                        "The configured database provider is not supported."),
                };
                var listStatement = GeneratedTenantDataScopeComposer.ApplyDataScopeFilter(
                    baseStatement,
                    filter,
                    "AND TenantId = @TenantId");
                var rows = await queryExecutor
                    .QueryAsync<OrganizationLinkSourceRecord>(
                        listStatement,
                        GeneratedTenantDataScopeComposer.MergeParameters(
                            query.Parameters(offset, pageSize),
                            filter),
                        cancellationToken)
                    .ConfigureAwait(false);
                return Result<PagedResult<OrganizationLinkSourceResponse>>.Success(
                    new PagedResult<OrganizationLinkSourceResponse>(
                        rows.Select(Map).ToArray(),
                        page,
                        pageSize,
                        total));
            }

            public async Task<Result<OrganizationLinkSourceResponse>> GetByIdAsync(
                Guid organizationLinkSourceId,
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
                ConfigureReadDataScope(scope, currentUserId, ref filter);
                var statement = GeneratedTenantDataScopeComposer.ApplyDataScopeFilter(
                    OrganizationLinkSourceSql.FindByIdStatement,
                    filter,
                    "AND TenantId = @TenantId");
                var record = await queryExecutor
                    .QuerySingleOrDefaultAsync<OrganizationLinkSourceRecord>(
                        statement,
                        GeneratedTenantDataScopeComposer.MergeParameters(
                            new Dictionary<string, object?> { ["Id"] = organizationLinkSourceId },
                            filter),
                        cancellationToken)
                    .ConfigureAwait(false);
                return record is null
                    ? NotFound()
                    : Result<OrganizationLinkSourceResponse>.Success(Map(record));
            }

            internal async Task<Result<OrganizationLinkSourceResponse>> FindByIdAsync(
                Guid organizationLinkSourceId,
                CancellationToken cancellationToken = default)
            {
                var record = await queryExecutor
                    .QuerySingleOrDefaultAsync<OrganizationLinkSourceRecord>(
                        OrganizationLinkSourceSql.FindByIdStatement,
                        new Dictionary<string, object?> { ["Id"] = organizationLinkSourceId },
                        cancellationToken)
                    .ConfigureAwait(false);
                return record is null
                    ? NotFound()
                    : Result<OrganizationLinkSourceResponse>.Success(Map(record));
            }

    private static OrganizationLinkSourceResponse Map(
        OrganizationLinkSourceRecord record) =>
        new(
            record.Id,
            record.TenantId,
            record.OrganizationUnitId,
            record.Name,
            record.Version,
            record.IsDeleted,
            record.DeletedAtUtc,
            record.DeletedById);

    private static Result<OrganizationLinkSourceResponse> NotFound() =>
        OrganizationLinkSourceFeatureErrors.NotFound();

    // 在独立手写 partial 中细化业务记录范围；未实现时编译器移除调用。
    partial void ConfigureReadDataScope(
        EffectiveUserDataScope scope, Guid currentUserId, ref DataScopeSqlFilter? filter);

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

internal sealed partial class OrganizationLinkSourceManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    OrganizationLinkSourceQueryService queries,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator,
    IOrganizationOwnedEntityWriteAuthorizer writeAuthorizer)
{
    private async Task<Result<OrganizationLinkSourceResponse>> ExecuteWriteAsync(
        Func<CancellationToken, Task<Result<OrganizationLinkSourceResponse>>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transaction.ExecuteResultAsync(action, cancellationToken).ConfigureAwait(false);
        }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            return Result<OrganizationLinkSourceResponse>.Failure(new Error(
                "catalog.organization_sources.unique_conflict",
                "The unique field value already exists.", ErrorType.Conflict));
        }
    }
    public Task<Result<OrganizationLinkSourceResponse>> CreateAsync(
        CreateOrganizationLinkSourceRequest request,
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

    private async Task<Result<OrganizationLinkSourceResponse>> CreateCoreAsync(
        CreateOrganizationLinkSourceRequest request,
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
                    if (!authorization.IsSuccess || !authorization.Value)
                    {
                        return Result<OrganizationLinkSourceResponse>.Failure(authorization.Error ?? new Error(
                            OrganizationErrorCodes.WriteAccessDenied, "Write access to the organization unit was denied.", ErrorType.Forbidden));
                    }        var validationError = ValidateWriteRequest(
            request.Name);
        if (validationError is not null)
        {
            return validationError;
        }

        Error? domainError = null;
        ValidateCreateDomain(request, ref domainError);
        if (domainError is not null) return Result<OrganizationLinkSourceResponse>.Failure(domainError);
        var organizationLinkSourceId = idGenerator.NewId();

        var affectedRows = await commandExecutor.ExecuteAsync(
                OrganizationLinkSourceSql.InsertStatement,
                new Dictionary<string, object?>
                {
                    ["Id"] = organizationLinkSourceId,
                    ["OrganizationUnitId"] = organizationUnitId,
                    ["Name"] = request.Name,
                    ["Version"] = 1L,
                    ["IsDeleted"] = false,
                    ["DeletedAtUtc"] = (DateTimeOffset?)null,
                    ["DeletedById"] = (Guid?)null
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                "The generated insert must affect exactly one row.");
        }

        return await queries.FindByIdAsync(
                organizationLinkSourceId,
                cancellationToken)
            .ConfigureAwait(false);
    }


    // 领域读取只在存在实现时执行，普通生成 CRUD 不增加数据库往返。
    partial void ValidateUpdateDomain(Guid id, UpdateOrganizationLinkSourceRequest request,
        CancellationToken cancellationToken, ref Task<Error?>? validation);

    public Task<Result<OrganizationLinkSourceResponse>> UpdateAsync(
        Guid organizationLinkSourceId,
        UpdateOrganizationLinkSourceRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(
            token => UpdateCoreAsync(
                organizationLinkSourceId,
                request,
                actorUserId,
                token),
            cancellationToken);

    private async Task<Result<OrganizationLinkSourceResponse>> UpdateCoreAsync(
        Guid organizationLinkSourceId,
        UpdateOrganizationLinkSourceRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();

                var existingForAuthorization = await queries.FindByIdAsync(
                        organizationLinkSourceId,
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
                if (!authorization.IsSuccess || !authorization.Value)
                {
                    return Result<OrganizationLinkSourceResponse>.Failure(authorization.Error ?? new Error(
                        OrganizationErrorCodes.WriteAccessDenied, "Write access to the organization unit was denied.", ErrorType.Forbidden));
                }    var validationError = ValidateWriteRequest(
        request.Name);
    if (validationError is not null)
    {
        return validationError;
    }

        Task<Error?>? domainValidation = null;
        ValidateUpdateDomain(organizationLinkSourceId, request, cancellationToken, ref domainValidation);
        if (domainValidation is not null)
        {
            var domainError = await domainValidation.ConfigureAwait(false);
            if (domainError is not null)
            {
                return Result<OrganizationLinkSourceResponse>.Failure(domainError);
            }
        }

        var affectedRows = await commandExecutor.ExecuteAsync(
                OrganizationLinkSourceSql.UpdateStatement,
                new Dictionary<string, object?>
                {
                    ["Id"] = organizationLinkSourceId,
                    ["Name"] = request.Name,
                    ["Version"] = request.Version
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveWriteFailureAsync(
                    organizationLinkSourceId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return await queries.FindByIdAsync(
                organizationLinkSourceId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    // 删除约束先于级联写入，不能只在父行删除后判断领域状态。
    partial void ValidateDeleteDomain(OrganizationLinkSourceResponse existing, long? expectedVersion, ref Error? error);

    public Task<Result<OrganizationLinkSourceResponse>> DeleteAsync(
        Guid organizationLinkSourceId,
            DeleteOrganizationLinkSourceRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => DeleteCoreAsync(
                organizationLinkSourceId, request,
                actorUserId,
                token),
            cancellationToken);

    private async Task<Result<OrganizationLinkSourceResponse>> DeleteCoreAsync(
        Guid organizationLinkSourceId,
            DeleteOrganizationLinkSourceRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var existing = await queries.FindByIdAsync(
                organizationLinkSourceId,
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
                    if (!authorization.IsSuccess || !authorization.Value)
                    {
                        return Result<OrganizationLinkSourceResponse>.Failure(authorization.Error ?? new Error(
                            OrganizationErrorCodes.WriteAccessDenied, "Write access to the organization unit was denied.", ErrorType.Forbidden));
                    }
        Error? domainError = null;
        ValidateDeleteDomain(existing.Value!, request.Version, ref domainError);
        if (domainError is not null)
        {
            return Result<OrganizationLinkSourceResponse>.Failure(domainError);
        }

                var affectedRows = await commandExecutor.ExecuteAsync(
                OrganizationLinkSourceSql.DeleteStatement,
                new Dictionary<string, object?>
                {
                    ["Id"] = organizationLinkSourceId,
                    ["DeletedAtUtc"] = clock.UtcNow,
                    ["DeletedById"] = actorUserId,
                    ["Version"] = request.Version
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveWriteFailureAsync(
                    organizationLinkSourceId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return existing;
    }
    // 在独立手写 partial 文件实现领域约束；未实现时编译器移除调用。
    partial void ValidateCreateDomain(CreateOrganizationLinkSourceRequest request, ref Error? error);

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

    private static Result<OrganizationLinkSourceResponse>? ValidateWriteRequest(
        string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is { Length: > 64 })
        {
            return ValidationFailure("Name");
        }
        return null;
    }

    private static Result<OrganizationLinkSourceResponse> ValidationFailure(
        string field) =>
        Result<OrganizationLinkSourceResponse>.Failure(new Error(
            ValidationErrorCodes.Failed,
            "One or more generated field constraints were not satisfied.",
            ErrorType.Validation,
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [field] = ["The field value is invalid."],
            }, null, new[] { new ValidationViolation(field, "catalog.organization_sources.invalid_field", new Dictionary<string, object?>()) }));

    private async Task<Result<OrganizationLinkSourceResponse>> ResolveWriteFailureAsync(
        Guid organizationLinkSourceId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<OrganizationLinkSourceRecord>(
                OrganizationLinkSourceSql.FindByIdStatement,
                new Dictionary<string, object?> { ["Id"] = organizationLinkSourceId },
                cancellationToken)
            .ConfigureAwait(false);
        return record is null ? NotFound() : VersionConflict();
    }

    private static Result<OrganizationLinkSourceResponse> VersionConflict() =>
        Result<OrganizationLinkSourceResponse>.Failure(new Error(
            OrganizationLinkSourceErrorCodes.VersionConflict,
            "The resource was updated concurrently.",
            ErrorType.Conflict));

    private static Result<OrganizationLinkSourceResponse> NotFound() =>
        OrganizationLinkSourceFeatureErrors.NotFound();
}

internal static class OrganizationLinkSourceFeatureErrors
{
    internal static Result<OrganizationLinkSourceResponse> NotFound() =>
        Result<OrganizationLinkSourceResponse>.Failure(new Error(
            OrganizationLinkSourceErrorCodes.NotFound,
            "The resource was not found.",
            ErrorType.NotFound));
}
