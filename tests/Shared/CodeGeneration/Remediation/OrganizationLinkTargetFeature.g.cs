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

internal sealed partial class OrganizationLinkTargetQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions,
    IUserDataScopeResolver dataScopeResolver,
    IDataScopeSqlFilterBuilder dataScopeFilterBuilder)
{

            public async Task<Result<PagedResult<OrganizationLinkTargetResponse>>> ListAsync(
                Guid currentUserId,
                bool isSuperAdministrator,
                int page,
                int pageSize,
                CancellationToken cancellationToken = default,
                OrganizationLinkTargetListQuery? query = null)
            {
                    query ??= new();
                if (query.Validate() is { } queryError)
                    return Result<PagedResult<OrganizationLinkTargetResponse>>.Failure(queryError);
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
                    DatabaseProvider.SqlServer => OrganizationLinkTargetSql.QueryCountSqlServerStatement,
                    DatabaseProvider.MySql => OrganizationLinkTargetSql.QueryCountMySqlStatement,
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
                        OrganizationLinkTargetSql.ListSqlServerStatement,
                    DatabaseProvider.MySql =>
                        OrganizationLinkTargetSql.ListMySqlStatement,
                    _ => throw new InvalidOperationException(
                        "The configured database provider is not supported."),
                };
                var listStatement = GeneratedTenantDataScopeComposer.ApplyDataScopeFilter(
                    baseStatement,
                    filter,
                    "AND TenantId = @TenantId");
                var rows = await queryExecutor
                    .QueryAsync<OrganizationLinkTargetRecord>(
                        listStatement,
                        GeneratedTenantDataScopeComposer.MergeParameters(
                            query.Parameters(offset, pageSize),
                            filter),
                        cancellationToken)
                    .ConfigureAwait(false);
                return Result<PagedResult<OrganizationLinkTargetResponse>>.Success(
                    new PagedResult<OrganizationLinkTargetResponse>(
                        rows.Select(Map).ToArray(),
                        page,
                        pageSize,
                        total));
            }

            public async Task<Result<OrganizationLinkTargetResponse>> GetByIdAsync(
                Guid organizationLinkTargetId,
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
                    OrganizationLinkTargetSql.FindByIdStatement,
                    filter,
                    "AND TenantId = @TenantId");
                var record = await queryExecutor
                    .QuerySingleOrDefaultAsync<OrganizationLinkTargetRecord>(
                        statement,
                        GeneratedTenantDataScopeComposer.MergeParameters(
                            new Dictionary<string, object?> { ["Id"] = organizationLinkTargetId },
                            filter),
                        cancellationToken)
                    .ConfigureAwait(false);
                return record is null
                    ? NotFound()
                    : Result<OrganizationLinkTargetResponse>.Success(Map(record));
            }

            internal async Task<Result<OrganizationLinkTargetResponse>> FindByIdAsync(
                Guid organizationLinkTargetId,
                CancellationToken cancellationToken = default)
            {
                var record = await queryExecutor
                    .QuerySingleOrDefaultAsync<OrganizationLinkTargetRecord>(
                        OrganizationLinkTargetSql.FindByIdStatement,
                        new Dictionary<string, object?> { ["Id"] = organizationLinkTargetId },
                        cancellationToken)
                    .ConfigureAwait(false);
                return record is null
                    ? NotFound()
                    : Result<OrganizationLinkTargetResponse>.Success(Map(record));
            }

    private static OrganizationLinkTargetResponse Map(
        OrganizationLinkTargetRecord record) =>
        new(
            record.Id,
            record.TenantId,
            record.OrganizationUnitId,
            record.Name,
            record.Version,
            record.IsDeleted,
            record.DeletedAtUtc,
            record.DeletedById);

    private static Result<OrganizationLinkTargetResponse> NotFound() =>
        OrganizationLinkTargetFeatureErrors.NotFound();

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

internal sealed partial class OrganizationLinkTargetManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    OrganizationLinkTargetQueryService queries,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator,
    IOrganizationOwnedEntityWriteAuthorizer writeAuthorizer)
{
    private async Task<Result<OrganizationLinkTargetResponse>> ExecuteWriteAsync(
        Func<CancellationToken, Task<Result<OrganizationLinkTargetResponse>>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transaction.ExecuteResultAsync(action, cancellationToken).ConfigureAwait(false);
        }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            return Result<OrganizationLinkTargetResponse>.Failure(new Error(
                "catalog.organization_targets.unique_conflict",
                "The unique field value already exists.", ErrorType.Conflict));
        }
    }
    public Task<Result<OrganizationLinkTargetResponse>> CreateAsync(
        CreateOrganizationLinkTargetRequest request,
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

    private async Task<Result<OrganizationLinkTargetResponse>> CreateCoreAsync(
        CreateOrganizationLinkTargetRequest request,
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
                        return Result<OrganizationLinkTargetResponse>.Failure(authorization.Error ?? new Error(
                            OrganizationErrorCodes.WriteAccessDenied, "Write access to the organization unit was denied.", ErrorType.Forbidden));
                    }        var validationError = ValidateWriteRequest(
            request.Name);
        if (validationError is not null)
        {
            return validationError;
        }

        Error? domainError = null;
        ValidateCreateDomain(request, ref domainError);
        if (domainError is not null) return Result<OrganizationLinkTargetResponse>.Failure(domainError);
        var organizationLinkTargetId = idGenerator.NewId();

        var affectedRows = await commandExecutor.ExecuteAsync(
                OrganizationLinkTargetSql.InsertStatement,
                new Dictionary<string, object?>
                {
                    ["Id"] = organizationLinkTargetId,
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
                organizationLinkTargetId,
                cancellationToken)
            .ConfigureAwait(false);
    }


    // 领域读取只在存在实现时执行，普通生成 CRUD 不增加数据库往返。
    partial void ValidateUpdateDomain(Guid id, UpdateOrganizationLinkTargetRequest request,
        CancellationToken cancellationToken, ref Task<Error?>? validation);

    public Task<Result<OrganizationLinkTargetResponse>> UpdateAsync(
        Guid organizationLinkTargetId,
        UpdateOrganizationLinkTargetRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(
            token => UpdateCoreAsync(
                organizationLinkTargetId,
                request,
                actorUserId,
                token),
            cancellationToken);

    private async Task<Result<OrganizationLinkTargetResponse>> UpdateCoreAsync(
        Guid organizationLinkTargetId,
        UpdateOrganizationLinkTargetRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();

                var existingForAuthorization = await queries.FindByIdAsync(
                        organizationLinkTargetId,
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
                    return Result<OrganizationLinkTargetResponse>.Failure(authorization.Error ?? new Error(
                        OrganizationErrorCodes.WriteAccessDenied, "Write access to the organization unit was denied.", ErrorType.Forbidden));
                }    var validationError = ValidateWriteRequest(
        request.Name);
    if (validationError is not null)
    {
        return validationError;
    }

        Task<Error?>? domainValidation = null;
        ValidateUpdateDomain(organizationLinkTargetId, request, cancellationToken, ref domainValidation);
        if (domainValidation is not null)
        {
            var domainError = await domainValidation.ConfigureAwait(false);
            if (domainError is not null)
            {
                return Result<OrganizationLinkTargetResponse>.Failure(domainError);
            }
        }

        var affectedRows = await commandExecutor.ExecuteAsync(
                OrganizationLinkTargetSql.UpdateStatement,
                new Dictionary<string, object?>
                {
                    ["Id"] = organizationLinkTargetId,
                    ["Name"] = request.Name,
                    ["Version"] = request.Version
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveWriteFailureAsync(
                    organizationLinkTargetId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return await queries.FindByIdAsync(
                organizationLinkTargetId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    // 删除约束先于级联写入，不能只在父行删除后判断领域状态。
    partial void ValidateDeleteDomain(OrganizationLinkTargetResponse existing, long? expectedVersion, ref Error? error);

    public Task<Result<OrganizationLinkTargetResponse>> DeleteAsync(
        Guid organizationLinkTargetId,
            DeleteOrganizationLinkTargetRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => DeleteCoreAsync(
                organizationLinkTargetId, request,
                actorUserId,
                token),
            cancellationToken);

    private async Task<Result<OrganizationLinkTargetResponse>> DeleteCoreAsync(
        Guid organizationLinkTargetId,
            DeleteOrganizationLinkTargetRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var existing = await queries.FindByIdAsync(
                organizationLinkTargetId,
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
                        return Result<OrganizationLinkTargetResponse>.Failure(authorization.Error ?? new Error(
                            OrganizationErrorCodes.WriteAccessDenied, "Write access to the organization unit was denied.", ErrorType.Forbidden));
                    }
        Error? domainError = null;
        ValidateDeleteDomain(existing.Value!, request.Version, ref domainError);
        if (domainError is not null)
        {
            return Result<OrganizationLinkTargetResponse>.Failure(domainError);
        }

                var affectedRows = await commandExecutor.ExecuteAsync(
                OrganizationLinkTargetSql.DeleteStatement,
                new Dictionary<string, object?>
                {
                    ["Id"] = organizationLinkTargetId,
                    ["DeletedAtUtc"] = clock.UtcNow,
                    ["DeletedById"] = actorUserId,
                    ["Version"] = request.Version
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveWriteFailureAsync(
                    organizationLinkTargetId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return existing;
    }
    // 在独立手写 partial 文件实现领域约束；未实现时编译器移除调用。
    partial void ValidateCreateDomain(CreateOrganizationLinkTargetRequest request, ref Error? error);

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

    private static Result<OrganizationLinkTargetResponse>? ValidateWriteRequest(
        string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is { Length: > 64 })
        {
            return ValidationFailure("Name");
        }
        return null;
    }

    private static Result<OrganizationLinkTargetResponse> ValidationFailure(
        string field) =>
        Result<OrganizationLinkTargetResponse>.Failure(new Error(
            ValidationErrorCodes.Failed,
            "One or more generated field constraints were not satisfied.",
            ErrorType.Validation,
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [field] = ["The field value is invalid."],
            }, null, new[] { new ValidationViolation(field, "catalog.organization_targets.invalid_field", new Dictionary<string, object?>()) }));

    private async Task<Result<OrganizationLinkTargetResponse>> ResolveWriteFailureAsync(
        Guid organizationLinkTargetId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<OrganizationLinkTargetRecord>(
                OrganizationLinkTargetSql.FindByIdStatement,
                new Dictionary<string, object?> { ["Id"] = organizationLinkTargetId },
                cancellationToken)
            .ConfigureAwait(false);
        return record is null ? NotFound() : VersionConflict();
    }

    private static Result<OrganizationLinkTargetResponse> VersionConflict() =>
        Result<OrganizationLinkTargetResponse>.Failure(new Error(
            OrganizationLinkTargetErrorCodes.VersionConflict,
            "The resource was updated concurrently.",
            ErrorType.Conflict));

    private static Result<OrganizationLinkTargetResponse> NotFound() =>
        OrganizationLinkTargetFeatureErrors.NotFound();
}

internal static class OrganizationLinkTargetFeatureErrors
{
    internal static Result<OrganizationLinkTargetResponse> NotFound() =>
        Result<OrganizationLinkTargetResponse>.Failure(new Error(
            OrganizationLinkTargetErrorCodes.NotFound,
            "The resource was not found.",
            ErrorType.NotFound));
}
