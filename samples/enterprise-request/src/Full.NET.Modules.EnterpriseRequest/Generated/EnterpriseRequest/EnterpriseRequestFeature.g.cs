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

/// <summary>在本模块命令事务内检查业务不变量；仅访问本模块数据，禁止跨模块调用及外部副作用。</summary>
internal interface IEnterpriseRequestMutationPolicy
{
    Task<Error?> ValidateCreateAsync(CreateEnterpriseRequestRequest request, Guid actorUserId, Guid? organizationUnitId, CancellationToken cancellationToken);
    Task<Error?> ValidateUpdateAsync(Guid id, UpdateEnterpriseRequestRequest request, Guid actorUserId, CancellationToken cancellationToken);
    Task<Error?> ValidateDeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken);
}

internal sealed partial class EnterpriseRequestQueryService(
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
                CancellationToken cancellationToken = default,
                EnterpriseRequestListQuery? query = null)
            {
                    query ??= new();
                if (query.Validate() is { } queryError)
                    return Result<PagedResult<EnterpriseRequestResponse>>.Failure(queryError);
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
                    DatabaseProvider.SqlServer => EnterpriseRequestSql.QueryCountSqlServerStatement,
                    DatabaseProvider.MySql => EnterpriseRequestSql.QueryCountMySqlStatement,
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
                        EnterpriseRequestSql.ListSqlServerStatement,
                    DatabaseProvider.MySql =>
                        EnterpriseRequestSql.ListMySqlStatement,
                    _ => throw new InvalidOperationException(
                        "The configured database provider is not supported."),
                };
                var listStatement = GeneratedTenantDataScopeComposer.ApplyDataScopeFilter(
                    baseStatement,
                    filter,
                    "AND TenantId = @TenantId");
                var rows = await queryExecutor
                    .QueryAsync<EnterpriseRequestRecord>(
                        listStatement,
                        GeneratedTenantDataScopeComposer.MergeParameters(
                            query.Parameters(offset, pageSize),
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
                ConfigureReadDataScope(scope, currentUserId, ref filter);
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

internal sealed partial class EnterpriseRequestManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    EnterpriseRequestQueryService queries,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator,
    IEnterpriseRequestMutationPolicy mutationPolicy,
    IOrganizationOwnedEntityWriteAuthorizer writeAuthorizer)
{
    private async Task<Result<EnterpriseRequestResponse>> ExecuteWriteAsync(
        Func<CancellationToken, Task<Result<EnterpriseRequestResponse>>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transaction.ExecuteResultAsync(action, cancellationToken).ConfigureAwait(false);
        }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            return Result<EnterpriseRequestResponse>.Failure(new Error(
                "enterprise_request.enterprise_requests.unique_conflict",
                "The unique field value already exists.", ErrorType.Conflict));
        }
    }
    public Task<Result<EnterpriseRequestResponse>> CreateAsync(
        CreateEnterpriseRequestRequest request,
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
                    if (!authorization.IsSuccess || !authorization.Value)
                    {
                        return Result<EnterpriseRequestResponse>.Failure(authorization.Error ?? new Error(
                            OrganizationErrorCodes.WriteAccessDenied, "Write access to the organization unit was denied.", ErrorType.Forbidden));
                    }        var validationError = ValidateWriteRequest(
            request.RequestNumber,
            request.Title,
            request.Status,
            request.TotalAmount,
            request.ApplicantUserId);
        if (validationError is not null)
        {
            return validationError;
        }

        var mutationError = await mutationPolicy.ValidateCreateAsync(request, actorUserId, organizationUnitId, cancellationToken).ConfigureAwait(false);
        if (mutationError is not null) return Result<EnterpriseRequestResponse>.Failure(mutationError);

        Error? domainError = null;
        ValidateCreateDomain(request, ref domainError);
        if (domainError is not null) return Result<EnterpriseRequestResponse>.Failure(domainError);
        var enterpriseRequestId = idGenerator.NewId();

        var affectedRows = await commandExecutor.ExecuteAsync(
                EnterpriseRequestSql.InsertStatement,
                new Dictionary<string, object?>
                {
                    ["Id"] = enterpriseRequestId,
                    ["OrganizationUnitId"] = organizationUnitId,
                    ["RequestNumber"] = request.RequestNumber,
                    ["Title"] = request.Title,
                    ["Status"] = request.Status,
                    ["TotalAmount"] = request.TotalAmount,
                    ["ApplicantUserId"] = request.ApplicantUserId,
                    ["Version"] = 1L,
                    ["CreatedAtUtc"] = clock.UtcNow,
                    ["CreatedById"] = actorUserId,
                    ["UpdatedAtUtc"] = (DateTimeOffset?)null,
                    ["UpdatedById"] = (Guid?)null,
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
                enterpriseRequestId,
                cancellationToken)
            .ConfigureAwait(false);
    }


    // 领域读取只在存在实现时执行，普通生成 CRUD 不增加数据库往返。
    partial void ValidateUpdateDomain(Guid id, UpdateEnterpriseRequestRequest request,
        CancellationToken cancellationToken, ref Task<Error?>? validation);

    public Task<Result<EnterpriseRequestResponse>> UpdateAsync(
        Guid enterpriseRequestId,
        UpdateEnterpriseRequestRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(
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
                if (!authorization.IsSuccess || !authorization.Value)
                {
                    return Result<EnterpriseRequestResponse>.Failure(authorization.Error ?? new Error(
                        OrganizationErrorCodes.WriteAccessDenied, "Write access to the organization unit was denied.", ErrorType.Forbidden));
                }    var validationError = ValidateWriteRequest(
        request.RequestNumber,
        request.Title,
        request.Status,
        request.TotalAmount,
        request.ApplicantUserId);
    if (validationError is not null)
    {
        return validationError;
    }

        var mutationError = await mutationPolicy.ValidateUpdateAsync(enterpriseRequestId, request, actorUserId, cancellationToken).ConfigureAwait(false);
        if (mutationError is not null) return Result<EnterpriseRequestResponse>.Failure(mutationError);

        Task<Error?>? domainValidation = null;
        ValidateUpdateDomain(enterpriseRequestId, request, cancellationToken, ref domainValidation);
        if (domainValidation is not null)
        {
            var domainError = await domainValidation.ConfigureAwait(false);
            if (domainError is not null)
            {
                return Result<EnterpriseRequestResponse>.Failure(domainError);
            }
        }

        var affectedRows = await commandExecutor.ExecuteAsync(
                EnterpriseRequestSql.UpdateStatement,
                new Dictionary<string, object?>
                {
                    ["Id"] = enterpriseRequestId,
                    ["RequestNumber"] = request.RequestNumber,
                    ["Title"] = request.Title,
                    ["Status"] = request.Status,
                    ["TotalAmount"] = request.TotalAmount,
                    ["ApplicantUserId"] = request.ApplicantUserId,
                    ["UpdatedAtUtc"] = clock.UtcNow,
                    ["UpdatedById"] = actorUserId,
                    ["Version"] = request.Version
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

    // 删除约束先于级联写入，不能只在父行删除后判断领域状态。
    partial void ValidateDeleteDomain(EnterpriseRequestResponse existing, long? expectedVersion, ref Error? error);

    public Task<Result<EnterpriseRequestResponse>> DeleteAsync(
        Guid enterpriseRequestId,
            DeleteEnterpriseRequestRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
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
                    if (!authorization.IsSuccess || !authorization.Value)
                    {
                        return Result<EnterpriseRequestResponse>.Failure(authorization.Error ?? new Error(
                            OrganizationErrorCodes.WriteAccessDenied, "Write access to the organization unit was denied.", ErrorType.Forbidden));
                    }
        Error? domainError = null;
        ValidateDeleteDomain(existing.Value!, request.Version, ref domainError);
        if (domainError is not null)
        {
            return Result<EnterpriseRequestResponse>.Failure(domainError);
        }

        var mutationError = await mutationPolicy.ValidateDeleteAsync(enterpriseRequestId, actorUserId, cancellationToken).ConfigureAwait(false);
        if (mutationError is not null) return Result<EnterpriseRequestResponse>.Failure(mutationError);

        await CascadeDeleteDependentsAsync(
            existing.Value!,
            actorUserId,
            cancellationToken)
            .ConfigureAwait(false);
                var affectedRows = await commandExecutor.ExecuteAsync(
                EnterpriseRequestSql.DeleteStatement,
                new Dictionary<string, object?>
                {
                    ["Id"] = enterpriseRequestId,
                    ["DeletedAtUtc"] = clock.UtcNow,
                    ["DeletedById"] = actorUserId,
                    ["Version"] = request.Version
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
    // 在独立手写 partial 文件实现领域约束；未实现时编译器移除调用。
    partial void ValidateCreateDomain(CreateEnterpriseRequestRequest request, ref Error? error);

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
        string requestNumber,
        string title,
        string status,
        decimal totalAmount,
        Guid applicantUserId)
    {
        if (string.IsNullOrWhiteSpace(requestNumber) || requestNumber is { Length: > 64 })
        {
            return ValidationFailure("RequestNumber");
        }

        if (string.IsNullOrWhiteSpace(title) || title is { Length: > 200 })
        {
            return ValidationFailure("Title");
        }

        if (string.IsNullOrWhiteSpace(status) || status is { Length: > 32 })
        {
            return ValidationFailure("Status");
        }

        if ((totalAmount is decimal totalAmountValue && (decimal.Round(totalAmountValue, 2) != totalAmountValue || totalAmountValue <= -10000000000000000m || totalAmountValue >= 10000000000000000m)))
        {
            return ValidationFailure("TotalAmount");
        }

        if (applicantUserId == Guid.Empty)
        {
            return ValidationFailure("ApplicantUserId");
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
            }, null, new[] { new ValidationViolation(field, "enterprise_request.enterprise_requests.invalid_field", new Dictionary<string, object?>()) }));

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
                    EnterpriseRequestResponse principal,
                    Guid actorUserId,
                    CancellationToken cancellationToken)
                {
        await commandExecutor.ExecuteAsync(
                new SqlStatement(
                    "enterprise_request.cascade_delete_enterprise_request_line_requestId",
                    "UPDATE demo_enterprise_request_enterprise_request_line SET IsDeleted = 1, DeletedAtUtc = @DeletedAtUtc, DeletedById = @DeletedById, Version = Version + 1 WHERE RequestId = @Id AND TenantId = @TenantId AND IsDeleted = 0 AND OrganizationUnitId = @OrganizationUnitId",
                    SqlDataScope.TenantRequired,
                    SqlTenantBinding.CurrentTenantId),
                new Dictionary<string, object?>
                {
                    ["Id"] = principal.Id,
                                    ["TenantId"] = currentTenant.Id!.Value,
                    ["OrganizationUnitId"] = principal.OrganizationUnitId,
                    ["DeletedAtUtc"] = clock.UtcNow,
                    ["DeletedById"] = actorUserId,
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
