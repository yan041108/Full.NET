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

internal sealed class OrganizationTreeNodeQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions,
    IUserDataScopeResolver dataScopeResolver,
    IDataScopeSqlFilterBuilder dataScopeFilterBuilder)
{

            public async Task<Result<PagedResult<OrganizationTreeNodeResponse>>> ListAsync(
                Guid currentUserId,
                bool isSuperAdministrator,
                int page,
                int pageSize,
                CancellationToken cancellationToken = default,
                OrganizationTreeNodeListQuery? query = null)
            {
                    query ??= new();
                if (query.Validate() is { } queryError)
                    return Result<PagedResult<OrganizationTreeNodeResponse>>.Failure(queryError);
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
                    DatabaseProvider.SqlServer => OrganizationTreeNodeSql.QueryCountSqlServerStatement,
                    DatabaseProvider.MySql => OrganizationTreeNodeSql.QueryCountMySqlStatement,
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
                        OrganizationTreeNodeSql.ListSqlServerStatement,
                    DatabaseProvider.MySql =>
                        OrganizationTreeNodeSql.ListMySqlStatement,
                    _ => throw new InvalidOperationException(
                        "The configured database provider is not supported."),
                };
                var listStatement = GeneratedTenantDataScopeComposer.ApplyDataScopeFilter(
                    baseStatement,
                    filter,
                    "WHERE 1 = 1\n            AND TenantId = @TenantId");
                var rows = await queryExecutor
                    .QueryAsync<OrganizationTreeNodeRecord>(
                        listStatement,
                        GeneratedTenantDataScopeComposer.MergeParameters(
                            query.Parameters(offset, pageSize),
                            filter),
                        cancellationToken)
                    .ConfigureAwait(false);
                return Result<PagedResult<OrganizationTreeNodeResponse>>.Success(
                    new PagedResult<OrganizationTreeNodeResponse>(
                        rows.Select(Map).ToArray(),
                        page,
                        pageSize,
                        total));
            }

            public async Task<Result<OrganizationTreeNodeResponse>> GetByIdAsync(
                Guid organizationTreeNodeId,
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
                    OrganizationTreeNodeSql.FindByIdStatement,
                    filter,
                    "AND TenantId = @TenantId");
                var record = await queryExecutor
                    .QuerySingleOrDefaultAsync<OrganizationTreeNodeRecord>(
                        statement,
                        GeneratedTenantDataScopeComposer.MergeParameters(
                            new Dictionary<string, object?> { ["Id"] = organizationTreeNodeId },
                            filter),
                        cancellationToken)
                    .ConfigureAwait(false);
                return record is null
                    ? NotFound()
                    : Result<OrganizationTreeNodeResponse>.Success(Map(record));
            }

            internal async Task<Result<OrganizationTreeNodeResponse>> FindByIdAsync(
                Guid organizationTreeNodeId,
                CancellationToken cancellationToken = default)
            {
                var record = await queryExecutor
                    .QuerySingleOrDefaultAsync<OrganizationTreeNodeRecord>(
                        OrganizationTreeNodeSql.FindByIdStatement,
                        new Dictionary<string, object?> { ["Id"] = organizationTreeNodeId },
                        cancellationToken)
                    .ConfigureAwait(false);
                return record is null
                    ? NotFound()
                    : Result<OrganizationTreeNodeResponse>.Success(Map(record));
            }

    private static OrganizationTreeNodeResponse Map(
        OrganizationTreeNodeRecord record) =>
        new(
            record.Id,
            record.TenantId,
            record.OrganizationUnitId,
            record.Name,
            record.ParentId,
            record.Version,
            record.IsDeleted,
            record.DeletedAtUtc,
            record.DeletedById);

    private static Result<OrganizationTreeNodeResponse> NotFound() =>
        OrganizationTreeNodeFeatureErrors.NotFound();

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

internal sealed class OrganizationTreeNodeManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    OrganizationTreeNodeQueryService queries,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator,
    IOrganizationOwnedEntityWriteAuthorizer writeAuthorizer,
    IOptions<DatabaseOptions> databaseOptions)
{
    private async Task<Result<OrganizationTreeNodeResponse>> ExecuteWriteAsync(
        Func<CancellationToken, Task<Result<OrganizationTreeNodeResponse>>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transaction.ExecuteResultAsync(action, cancellationToken).ConfigureAwait(false);
        }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            return Result<OrganizationTreeNodeResponse>.Failure(new Error(
                "catalog.organization_tree_nodes.unique_conflict",
                "The unique field value already exists.", ErrorType.Conflict));
        }
    }
    public Task<Result<OrganizationTreeNodeResponse>> CreateAsync(
        CreateOrganizationTreeNodeRequest request,
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

    private async Task<Result<OrganizationTreeNodeResponse>> CreateCoreAsync(
        CreateOrganizationTreeNodeRequest request,
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
                        return Result<OrganizationTreeNodeResponse>.Failure(authorization.Error!);
                    }        var validationError = ValidateWriteRequest(
            request.Name);
        if (validationError is not null)
        {
            return validationError;
        }

        var organizationTreeNodeId = idGenerator.NewId();

        await AcquireTreeWriteLockAsync(cancellationToken).ConfigureAwait(false);

        var treeError = await EnsureTreeParentAsync(
            organizationTreeNodeId,
            request.ParentId,
            organizationUnitId,
            cancellationToken)
            .ConfigureAwait(false);
        if (treeError is not null)
        {
            return treeError;
        }

        var affectedRows = await commandExecutor.ExecuteAsync(
                OrganizationTreeNodeSql.InsertStatement,
                new Dictionary<string, object?> { ["Id"] = organizationTreeNodeId, ["OrganizationUnitId"] = organizationUnitId, ["Name"] = request.Name, ["ParentId"] = request.ParentId, ["Version"] = 1L, ["IsDeleted"] = false, ["DeletedAtUtc"] = (DateTimeOffset?)null, ["DeletedById"] = (Guid?)null },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                "The generated insert must affect exactly one row.");
        }

        return await queries.FindByIdAsync(
                organizationTreeNodeId,
                cancellationToken)
            .ConfigureAwait(false);
    }


    public Task<Result<OrganizationTreeNodeResponse>> UpdateAsync(
        Guid organizationTreeNodeId,
        UpdateOrganizationTreeNodeRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(
            token => UpdateCoreAsync(
                organizationTreeNodeId,
                request,
                actorUserId,
                token),
            cancellationToken);

    private async Task<Result<OrganizationTreeNodeResponse>> UpdateCoreAsync(
        Guid organizationTreeNodeId,
        UpdateOrganizationTreeNodeRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();

                var existingForAuthorization = await queries.FindByIdAsync(
                        organizationTreeNodeId,
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
                    return Result<OrganizationTreeNodeResponse>.Failure(authorization.Error!);
                }    var validationError = ValidateWriteRequest(
        request.Name);
    if (validationError is not null)
    {
        return validationError;
    }

        await AcquireTreeWriteLockAsync(cancellationToken).ConfigureAwait(false);

        var treeError = await EnsureTreeParentAsync(
            organizationTreeNodeId,
            request.ParentId,
            existingForAuthorization.Value!.OrganizationUnitId,
            cancellationToken)
            .ConfigureAwait(false);
        if (treeError is not null)
        {
            return treeError;
        }

        var affectedRows = await commandExecutor.ExecuteAsync(
                OrganizationTreeNodeSql.UpdateStatement,
                new Dictionary<string, object?> { ["Id"] = organizationTreeNodeId, ["Name"] = request.Name, ["ParentId"] = request.ParentId, ["Version"] = request.Version },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveWriteFailureAsync(
                    organizationTreeNodeId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return await queries.FindByIdAsync(
                organizationTreeNodeId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<Result<OrganizationTreeNodeResponse>> DeleteAsync(
        Guid organizationTreeNodeId,
            DeleteOrganizationTreeNodeRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(
            token => DeleteCoreAsync(
                organizationTreeNodeId, request,
                actorUserId,
                token),
            cancellationToken);

    private async Task<Result<OrganizationTreeNodeResponse>> DeleteCoreAsync(
        Guid organizationTreeNodeId,
            DeleteOrganizationTreeNodeRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var existing = await queries.FindByIdAsync(
                organizationTreeNodeId,
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
                        return Result<OrganizationTreeNodeResponse>.Failure(authorization.Error!);
                    }
            await AcquireTreeWriteLockAsync(cancellationToken).ConfigureAwait(false);
        var treeChildrenError = await EnsureTreeHasNoChildrenAsync(organizationTreeNodeId, cancellationToken).ConfigureAwait(false);
        if (treeChildrenError is not null) return treeChildrenError;

                var affectedRows = await commandExecutor.ExecuteAsync(
                OrganizationTreeNodeSql.DeleteStatement,
                new Dictionary<string, object?> { ["Id"] = organizationTreeNodeId, ["DeletedAtUtc"] = clock.UtcNow, ["DeletedById"] = actorUserId, ["Version"] = request.Version },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveWriteFailureAsync(
                    organizationTreeNodeId,
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

    private static Result<OrganizationTreeNodeResponse>? ValidateWriteRequest(
        string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is { Length: > 64 })
        {
            return ValidationFailure("Name");
        }
        return null;
    }

    private static Result<OrganizationTreeNodeResponse> ValidationFailure(
        string field) =>
        Result<OrganizationTreeNodeResponse>.Failure(new Error(
            ValidationErrorCodes.Failed,
            "One or more generated field constraints were not satisfied.",
            ErrorType.Validation,
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [field] = ["The field value is invalid."],
            }, null, new[] { new ValidationViolation(field, "catalog.organization_tree_nodes.invalid_field", new Dictionary<string, object?>()) }));

    private async Task<Result<OrganizationTreeNodeResponse>> ResolveWriteFailureAsync(
        Guid organizationTreeNodeId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<OrganizationTreeNodeRecord>(
                OrganizationTreeNodeSql.FindByIdStatement,
                new Dictionary<string, object?> { ["Id"] = organizationTreeNodeId },
                cancellationToken)
            .ConfigureAwait(false);
        return record is null ? NotFound() : VersionConflict();
    }

    private static Result<OrganizationTreeNodeResponse> VersionConflict() =>
        Result<OrganizationTreeNodeResponse>.Failure(new Error(
            OrganizationTreeNodeErrorCodes.VersionConflict,
            "The resource was updated concurrently.",
            ErrorType.Conflict));
private async Task AcquireTreeWriteLockAsync(CancellationToken cancellationToken)
{
    // 锁行唯一键使不同节点的写操作共用同一事务互斥；在环检查和结构修改前取得。
    var sql = databaseOptions.Value.Provider == DatabaseProvider.MySql
        ? "INSERT INTO acme_catalog_organization_tree_node_tree_guard (Id, TenantId) VALUES (@Id, @TenantId) ON DUPLICATE KEY UPDATE Id = Id;" : "IF NOT EXISTS (SELECT Id FROM acme_catalog_organization_tree_node_tree_guard WITH (UPDLOCK, HOLDLOCK) WHERE TenantId = @TenantId) INSERT INTO acme_catalog_organization_tree_node_tree_guard (Id, TenantId) VALUES (@Id, @TenantId);";
    await commandExecutor.ExecuteAsync(new SqlStatement("catalog.organization_tree_node.tree_write_lock", sql, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId),
        new Dictionary<string, object?> { ["Id"] = idGenerator.NewId(), ["TenantId"] = currentTenant.Id!.Value }, cancellationToken).ConfigureAwait(false);
}

private async Task<Result<OrganizationTreeNodeResponse>?> EnsureTreeHasNoChildrenAsync(Guid id, CancellationToken cancellationToken)
{
    var count = await queryExecutor.QuerySingleOrDefaultAsync<long>(new SqlStatement(
        "catalog.organization_tree_node.tree_children",
        "SELECT COUNT(*) FROM acme_catalog_organization_tree_node WHERE ParentId = @Id AND TenantId = @TenantId AND IsDeleted = 0",
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId), new Dictionary<string, object?> { ["Id"] = id, ["TenantId"] = currentTenant.Id!.Value }, cancellationToken).ConfigureAwait(false);
    return count == 0 ? null : Result<OrganizationTreeNodeResponse>.Failure(new Error(
        "catalog.organization_tree_nodes.parent_has_children", "Remove or move the child nodes first.", ErrorType.Conflict));
}

private async Task<Result<OrganizationTreeNodeResponse>?> EnsureTreeParentAsync(
    Guid organizationTreeNodeId,
    Guid? parentId,
    Guid organizationUnitId,
    CancellationToken cancellationToken)
{
    if (parentId is null)
    {
        return null;
    }

    if (parentId.Value == organizationTreeNodeId)
    {
        return OrganizationTreeNodeFeatureErrors.InvalidParent();
    }

    var parent = await queryExecutor
        .QuerySingleOrDefaultAsync<OrganizationTreeNodeRecord>(
            OrganizationTreeNodeSql.FindByIdStatement,
            new Dictionary<string, object?> { ["Id"] = parentId.Value, ["TenantId"] = currentTenant.Id!.Value },
            cancellationToken)
        .ConfigureAwait(false);
    if (parent is null || parent.OrganizationUnitId != organizationUnitId)
    {
        return OrganizationTreeNodeFeatureErrors.InvalidParent();
    }

    var current = parentId.Value;
    for (var depth = 0; depth < 32; depth++)
    {
        var node = await queryExecutor
            .QuerySingleOrDefaultAsync<OrganizationTreeNodeRecord>(
                OrganizationTreeNodeSql.FindByIdStatement,
                new Dictionary<string, object?> { ["Id"] = current, ["TenantId"] = currentTenant.Id!.Value },
                cancellationToken)
            .ConfigureAwait(false);
        if (node is null || node.OrganizationUnitId != organizationUnitId)
        {
            return OrganizationTreeNodeFeatureErrors.InvalidParent();
        }
        if (node.ParentId is null)
        {
            return null;
        }

        if (node.ParentId.Value == organizationTreeNodeId)
        {
            return OrganizationTreeNodeFeatureErrors.ParentCycle();
        }

        current = node.ParentId.Value;
    }

    return OrganizationTreeNodeFeatureErrors.ParentCycle();
}

    private static Result<OrganizationTreeNodeResponse> NotFound() =>
        OrganizationTreeNodeFeatureErrors.NotFound();
}

internal static class OrganizationTreeNodeFeatureErrors
{
    internal static Result<OrganizationTreeNodeResponse> NotFound() =>
        Result<OrganizationTreeNodeResponse>.Failure(new Error(
            OrganizationTreeNodeErrorCodes.NotFound,
            "The resource was not found.",
            ErrorType.NotFound));

internal static Result<OrganizationTreeNodeResponse> InvalidParent() =>
    Result<OrganizationTreeNodeResponse>.Failure(new Error(
        OrganizationTreeNodeErrorCodes.InvalidParent,
        "The parent node is missing or outside the current tenant.",
        ErrorType.Validation));

internal static Result<OrganizationTreeNodeResponse> ParentCycle() =>
    Result<OrganizationTreeNodeResponse>.Failure(new Error(
        OrganizationTreeNodeErrorCodes.ParentCycle,
        "The parent assignment would create a cycle.",
        ErrorType.Validation));
}
