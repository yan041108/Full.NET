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
internal interface IEnterpriseRequestDetailMutationPolicy
{
    Task<Error?> ValidateCreateAsync(CreateEnterpriseRequestDetailRequest request, Guid actorUserId, Guid? organizationUnitId, CancellationToken cancellationToken);
    Task<Error?> ValidateUpdateAsync(Guid id, UpdateEnterpriseRequestDetailRequest request, Guid actorUserId, CancellationToken cancellationToken);
    Task<Error?> ValidateDeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken);
}

internal sealed class EnterpriseRequestDetailQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions,
    IUserDataScopeResolver dataScopeResolver,
    IDataScopeSqlFilterBuilder dataScopeFilterBuilder)
{

            public async Task<Result<PagedResult<EnterpriseRequestDetailResponse>>> ListAsync(
                Guid currentUserId,
                bool isSuperAdministrator,
                int page,
                int pageSize,
                CancellationToken cancellationToken = default,
                EnterpriseRequestDetailListQuery? query = null)
            {
                    query ??= new();
                if (query.Validate() is { } queryError)
                    return Result<PagedResult<EnterpriseRequestDetailResponse>>.Failure(queryError);
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
                    DatabaseProvider.SqlServer => EnterpriseRequestDetailSql.QueryCountSqlServerStatement,
                    DatabaseProvider.MySql => EnterpriseRequestDetailSql.QueryCountMySqlStatement,
                    _ => throw new InvalidOperationException("The configured database provider is not supported."),
                };
                // 计数与分页共用固定租户条件追加范围，不依赖 WHERE 前导项或排版。
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
                        EnterpriseRequestDetailSql.ListSqlServerStatement,
                    DatabaseProvider.MySql =>
                        EnterpriseRequestDetailSql.ListMySqlStatement,
                    _ => throw new InvalidOperationException(
                        "The configured database provider is not supported."),
                };
                var listStatement = GeneratedTenantDataScopeComposer.ApplyDataScopeFilter(
                    baseStatement,
                    filter,
                    "AND TenantId = @TenantId");
                var rows = await queryExecutor
                    .QueryAsync<EnterpriseRequestDetailRecord>(
                        listStatement,
                        GeneratedTenantDataScopeComposer.MergeParameters(
                            query.Parameters(offset, pageSize),
                            filter),
                        cancellationToken)
                    .ConfigureAwait(false);
                return Result<PagedResult<EnterpriseRequestDetailResponse>>.Success(
                    new PagedResult<EnterpriseRequestDetailResponse>(
                        rows.Select(Map).ToArray(),
                        page,
                        pageSize,
                        total));
            }

            public async Task<Result<EnterpriseRequestDetailResponse>> GetByIdAsync(
                Guid enterpriseRequestDetailId,
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
                    EnterpriseRequestDetailSql.FindByIdStatement,
                    filter,
                    "AND TenantId = @TenantId");
                var record = await queryExecutor
                    .QuerySingleOrDefaultAsync<EnterpriseRequestDetailRecord>(
                        statement,
                        GeneratedTenantDataScopeComposer.MergeParameters(
                            new Dictionary<string, object?> { ["Id"] = enterpriseRequestDetailId },
                            filter),
                        cancellationToken)
                    .ConfigureAwait(false);
                return record is null
                    ? NotFound()
                    : Result<EnterpriseRequestDetailResponse>.Success(Map(record));
            }

            internal async Task<Result<EnterpriseRequestDetailResponse>> FindByIdAsync(
                Guid enterpriseRequestDetailId,
                CancellationToken cancellationToken = default)
            {
                var record = await queryExecutor
                    .QuerySingleOrDefaultAsync<EnterpriseRequestDetailRecord>(
                        EnterpriseRequestDetailSql.FindByIdStatement,
                        new Dictionary<string, object?> { ["Id"] = enterpriseRequestDetailId },
                        cancellationToken)
                    .ConfigureAwait(false);
                return record is null
                    ? NotFound()
                    : Result<EnterpriseRequestDetailResponse>.Success(Map(record));
            }

    private static EnterpriseRequestDetailResponse Map(
        EnterpriseRequestDetailRecord record) =>
        new(
            record.Id,
            record.TenantId,
            record.OrganizationUnitId,
            record.RequestId,
            record.LineNumber,
            record.ItemDescription,
            record.Quantity,
            record.UnitPrice,
            record.LineAmount,
            record.Version,
            record.CreatedAtUtc,
            record.CreatedById,
            record.UpdatedAtUtc,
            record.UpdatedById,
            record.IsDeleted,
            record.DeletedAtUtc,
            record.DeletedById);

    private static Result<EnterpriseRequestDetailResponse> NotFound() =>
        EnterpriseRequestDetailFeatureErrors.NotFound();

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

internal sealed class EnterpriseRequestDetailManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    EnterpriseRequestDetailQueryService queries,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator,
    IEnterpriseRequestDetailMutationPolicy mutationPolicy,
    IOrganizationOwnedEntityWriteAuthorizer writeAuthorizer)
{
    private async Task<Result<EnterpriseRequestDetailResponse>> ExecuteWriteAsync(
        Func<CancellationToken, Task<Result<EnterpriseRequestDetailResponse>>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transaction.ExecuteResultAsync(action, cancellationToken).ConfigureAwait(false);
        }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            return Result<EnterpriseRequestDetailResponse>.Failure(new Error(
                "enterprise_request.enterprise_request_lines.unique_conflict",
                "The unique field value already exists.", ErrorType.Conflict));
        }
    }
    public Task<Result<EnterpriseRequestDetailResponse>> CreateAsync(
        CreateEnterpriseRequestDetailRequest request,
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

    private async Task<Result<EnterpriseRequestDetailResponse>> CreateCoreAsync(
        CreateEnterpriseRequestDetailRequest request,
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
                        return Result<EnterpriseRequestDetailResponse>.Failure(authorization.Error ?? new Error(
                            OrganizationErrorCodes.WriteAccessDenied, "Write access to the organization unit was denied.", ErrorType.Forbidden));
                    }        var validationError = ValidateWriteRequest(
            request.RequestId,
            request.ItemDescription,
            request.Quantity,
            request.UnitPrice,
            request.LineAmount);
        if (validationError is not null)
        {
            return validationError;
        }

        var mutationError = await mutationPolicy.ValidateCreateAsync(request, actorUserId, organizationUnitId, cancellationToken).ConfigureAwait(false);
        if (mutationError is not null) return Result<EnterpriseRequestDetailResponse>.Failure(mutationError);

        var enterpriseRequestDetailId = idGenerator.NewId();

        var affectedRows = await commandExecutor.ExecuteAsync(
                EnterpriseRequestDetailSql.InsertStatement,
                new Dictionary<string, object?> { ["Id"] = enterpriseRequestDetailId, ["OrganizationUnitId"] = organizationUnitId, ["RequestId"] = request.RequestId, ["LineNumber"] = request.LineNumber, ["ItemDescription"] = request.ItemDescription, ["Quantity"] = request.Quantity, ["UnitPrice"] = request.UnitPrice, ["LineAmount"] = request.LineAmount, ["Version"] = 1L, ["CreatedAtUtc"] = clock.UtcNow, ["CreatedById"] = actorUserId, ["UpdatedAtUtc"] = (DateTimeOffset?)null, ["UpdatedById"] = (Guid?)null, ["IsDeleted"] = false, ["DeletedAtUtc"] = (DateTimeOffset?)null, ["DeletedById"] = (Guid?)null },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                "The generated insert must affect exactly one row.");
        }

        return await queries.FindByIdAsync(
                enterpriseRequestDetailId,
                cancellationToken)
            .ConfigureAwait(false);
    }


    public Task<Result<EnterpriseRequestDetailResponse>> UpdateAsync(
        Guid enterpriseRequestDetailId,
        UpdateEnterpriseRequestDetailRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(
            token => UpdateCoreAsync(
                enterpriseRequestDetailId,
                request,
                actorUserId,
                token),
            cancellationToken);

    private async Task<Result<EnterpriseRequestDetailResponse>> UpdateCoreAsync(
        Guid enterpriseRequestDetailId,
        UpdateEnterpriseRequestDetailRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();

                var existingForAuthorization = await queries.FindByIdAsync(
                        enterpriseRequestDetailId,
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
                    return Result<EnterpriseRequestDetailResponse>.Failure(authorization.Error ?? new Error(
                        OrganizationErrorCodes.WriteAccessDenied, "Write access to the organization unit was denied.", ErrorType.Forbidden));
                }    var validationError = ValidateWriteRequest(
        request.RequestId,
        request.ItemDescription,
        request.Quantity,
        request.UnitPrice,
        request.LineAmount);
    if (validationError is not null)
    {
        return validationError;
    }

        var mutationError = await mutationPolicy.ValidateUpdateAsync(enterpriseRequestDetailId, request, actorUserId, cancellationToken).ConfigureAwait(false);
        if (mutationError is not null) return Result<EnterpriseRequestDetailResponse>.Failure(mutationError);

        var affectedRows = await commandExecutor.ExecuteAsync(
                EnterpriseRequestDetailSql.UpdateStatement,
                new Dictionary<string, object?> { ["Id"] = enterpriseRequestDetailId, ["RequestId"] = request.RequestId, ["LineNumber"] = request.LineNumber, ["ItemDescription"] = request.ItemDescription, ["Quantity"] = request.Quantity, ["UnitPrice"] = request.UnitPrice, ["LineAmount"] = request.LineAmount, ["UpdatedAtUtc"] = clock.UtcNow, ["UpdatedById"] = actorUserId, ["Version"] = request.Version },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveWriteFailureAsync(
                    enterpriseRequestDetailId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return await queries.FindByIdAsync(
                enterpriseRequestDetailId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<Result<EnterpriseRequestDetailResponse>> DeleteAsync(
        Guid enterpriseRequestDetailId,
            DeleteEnterpriseRequestDetailRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(
            token => DeleteCoreAsync(
                enterpriseRequestDetailId, request,
                actorUserId,
                token),
            cancellationToken);

    private async Task<Result<EnterpriseRequestDetailResponse>> DeleteCoreAsync(
        Guid enterpriseRequestDetailId,
            DeleteEnterpriseRequestDetailRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var existing = await queries.FindByIdAsync(
                enterpriseRequestDetailId,
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
                        return Result<EnterpriseRequestDetailResponse>.Failure(authorization.Error ?? new Error(
                            OrganizationErrorCodes.WriteAccessDenied, "Write access to the organization unit was denied.", ErrorType.Forbidden));
                    }
        var mutationError = await mutationPolicy.ValidateDeleteAsync(enterpriseRequestDetailId, actorUserId, cancellationToken).ConfigureAwait(false);
        if (mutationError is not null) return Result<EnterpriseRequestDetailResponse>.Failure(mutationError);

                var affectedRows = await commandExecutor.ExecuteAsync(
                EnterpriseRequestDetailSql.DeleteStatement,
                new Dictionary<string, object?> { ["Id"] = enterpriseRequestDetailId, ["DeletedAtUtc"] = clock.UtcNow, ["DeletedById"] = actorUserId, ["Version"] = request.Version },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveWriteFailureAsync(
                    enterpriseRequestDetailId,
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

    private static Result<EnterpriseRequestDetailResponse>? ValidateWriteRequest(
        Guid requestId,
        string itemDescription,
        decimal quantity,
        decimal unitPrice,
        decimal lineAmount)
    {
        if (requestId == Guid.Empty)
        {
            return ValidationFailure("RequestId");
        }

        if (string.IsNullOrWhiteSpace(itemDescription) || itemDescription is { Length: > 200 })
        {
            return ValidationFailure("ItemDescription");
        }

        if ((quantity is decimal quantityValue && (decimal.Round(quantityValue, 4) != quantityValue || quantityValue <= -100000000000000m || quantityValue >= 100000000000000m)))
        {
            return ValidationFailure("Quantity");
        }

        if ((unitPrice is decimal unitPriceValue && (decimal.Round(unitPriceValue, 2) != unitPriceValue || unitPriceValue <= -10000000000000000m || unitPriceValue >= 10000000000000000m)))
        {
            return ValidationFailure("UnitPrice");
        }

        if ((lineAmount is decimal lineAmountValue && (decimal.Round(lineAmountValue, 2) != lineAmountValue || lineAmountValue <= -10000000000000000m || lineAmountValue >= 10000000000000000m)))
        {
            return ValidationFailure("LineAmount");
        }
        return null;
    }

    private static Result<EnterpriseRequestDetailResponse> ValidationFailure(
        string field) =>
        Result<EnterpriseRequestDetailResponse>.Failure(new Error(
            ValidationErrorCodes.Failed,
            "One or more generated field constraints were not satisfied.",
            ErrorType.Validation,
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [field] = ["The field value is invalid."],
            }, null, new[] { new ValidationViolation(field, "enterprise_request.enterprise_request_lines.invalid_field", new Dictionary<string, object?>()) }));

    private async Task<Result<EnterpriseRequestDetailResponse>> ResolveWriteFailureAsync(
        Guid enterpriseRequestDetailId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<EnterpriseRequestDetailRecord>(
                EnterpriseRequestDetailSql.FindByIdStatement,
                new Dictionary<string, object?> { ["Id"] = enterpriseRequestDetailId },
                cancellationToken)
            .ConfigureAwait(false);
        return record is null ? NotFound() : VersionConflict();
    }

    private static Result<EnterpriseRequestDetailResponse> VersionConflict() =>
        Result<EnterpriseRequestDetailResponse>.Failure(new Error(
            EnterpriseRequestDetailErrorCodes.VersionConflict,
            "The resource was updated concurrently.",
            ErrorType.Conflict));

    private static Result<EnterpriseRequestDetailResponse> NotFound() =>
        EnterpriseRequestDetailFeatureErrors.NotFound();
}

internal static class EnterpriseRequestDetailFeatureErrors
{
    internal static Result<EnterpriseRequestDetailResponse> NotFound() =>
        Result<EnterpriseRequestDetailResponse>.Failure(new Error(
            EnterpriseRequestDetailErrorCodes.NotFound,
            "The resource was not found.",
            ErrorType.NotFound));
}
