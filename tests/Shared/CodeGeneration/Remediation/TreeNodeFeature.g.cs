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

namespace Acme.Remediation.Generated;

internal sealed class TreeNodeQueryService(
    IQueryExecutor queryExecutor,
    IMultiResultQueryExecutor multiResultQueryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{

            public async Task<Result<PagedResult<TreeNodeResponse>>> ListAsync(
                int page,
                int pageSize,
                CancellationToken cancellationToken = default,
                TreeNodeListQuery? query = null)
            {
                    query ??= new();
                if (query.Validate() is { } queryError)
                    return Result<PagedResult<TreeNodeResponse>>.Failure(queryError);
                page = Math.Max(page, 1);
                pageSize = Math.Clamp(pageSize, 1, 100);
                var offset = (long)(page - 1) * pageSize;
                var statement = databaseOptions.Value.Provider switch
                {
                    DatabaseProvider.SqlServer =>
                        TreeNodeSql.PageSqlServerStatement,
                    DatabaseProvider.MySql =>
                        TreeNodeSql.PageMySqlStatement,
                    _ => throw new InvalidOperationException(
                        "The configured database provider is not supported."),
                };
                var pageResult = await multiResultQueryExecutor.QueryMultipleAsync(
                        statement,
                        query.Parameters(offset, pageSize),
                        async (reader, _) =>
                        {
                            var total = await reader.ReadSingleOrDefaultAsync<long>()
                                .ConfigureAwait(false);
                            var rows = await reader
                                .ReadAsync<TreeNodeRecord>()
                                .ConfigureAwait(false);
                            return (Total: total, Rows: rows);
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
                return Result<PagedResult<TreeNodeResponse>>.Success(
                    new PagedResult<TreeNodeResponse>(
                        pageResult.Rows.Select(Map).ToArray(),
                        page,
                        pageSize,
                        pageResult.Total));
            }

            public async Task<Result<TreeNodeResponse>> GetByIdAsync(
                Guid treeNodeId,
                CancellationToken cancellationToken = default)
            {
                var record = await queryExecutor
                    .QuerySingleOrDefaultAsync<TreeNodeRecord>(
                        TreeNodeSql.FindByIdStatement,
                        new Dictionary<string, object?> { ["Id"] = treeNodeId },
                        cancellationToken)
                    .ConfigureAwait(false);
                return record is null
                    ? NotFound()
                    : Result<TreeNodeResponse>.Success(Map(record));
            }

        internal Task<Result<TreeNodeResponse>> FindByIdAsync(
            Guid treeNodeId,
            CancellationToken cancellationToken = default) =>
            GetByIdAsync(treeNodeId, cancellationToken);

    private static TreeNodeResponse Map(
        TreeNodeRecord record) =>
        new(
            record.Id,
            record.TenantId,
            record.Name,
            record.ParentId,
            record.Version,
            record.IsDeleted,
            record.DeletedAtUtc,
            record.DeletedById);

    private static Result<TreeNodeResponse> NotFound() =>
        TreeNodeFeatureErrors.NotFound();

}

internal sealed class TreeNodeManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    TreeNodeQueryService queries,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<DatabaseOptions> databaseOptions)
{
    private async Task<Result<TreeNodeResponse>> ExecuteWriteAsync(
        Func<CancellationToken, Task<Result<TreeNodeResponse>>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transaction.ExecuteResultAsync(action, cancellationToken).ConfigureAwait(false);
        }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            return Result<TreeNodeResponse>.Failure(new Error(
                "catalog.tree_nodes.unique_conflict",
                "The unique field value already exists.", ErrorType.Conflict));
        }
    }
    public Task<Result<TreeNodeResponse>> CreateAsync(
        CreateTreeNodeRequest request,
Guid actorUserId,
CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(
            token => CreateCoreAsync(
                request,
                actorUserId,

                token),
            cancellationToken);

    private async Task<Result<TreeNodeResponse>> CreateCoreAsync(
        CreateTreeNodeRequest request,
Guid actorUserId,
CancellationToken cancellationToken = default)
    {
        EnsureTenantContext();
        var validationError = ValidateWriteRequest(
            request.Name);
        if (validationError is not null)
        {
            return validationError;
        }

        var treeNodeId = idGenerator.NewId();

        await AcquireTreeWriteLockAsync(cancellationToken).ConfigureAwait(false);

        var treeError = await EnsureTreeParentAsync(
            treeNodeId,
            request.ParentId,
            cancellationToken)
            .ConfigureAwait(false);
        if (treeError is not null)
        {
            return treeError;
        }

        var affectedRows = await commandExecutor.ExecuteAsync(
                TreeNodeSql.InsertStatement,
                new Dictionary<string, object?> { ["Id"] = treeNodeId, ["Name"] = request.Name, ["ParentId"] = request.ParentId, ["Version"] = 1L, ["IsDeleted"] = false, ["DeletedAtUtc"] = (DateTimeOffset?)null, ["DeletedById"] = (Guid?)null },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                "The generated insert must affect exactly one row.");
        }

        return await queries.FindByIdAsync(
                treeNodeId,
                cancellationToken)
            .ConfigureAwait(false);
    }


    public Task<Result<TreeNodeResponse>> UpdateAsync(
        Guid treeNodeId,
        UpdateTreeNodeRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(
            token => UpdateCoreAsync(
                treeNodeId,
                request,
                actorUserId,
                token),
            cancellationToken);

    private async Task<Result<TreeNodeResponse>> UpdateCoreAsync(
        Guid treeNodeId,
        UpdateTreeNodeRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
    var validationError = ValidateWriteRequest(
        request.Name);
    if (validationError is not null)
    {
        return validationError;
    }

        await AcquireTreeWriteLockAsync(cancellationToken).ConfigureAwait(false);

        var treeError = await EnsureTreeParentAsync(
            treeNodeId,
            request.ParentId,
            cancellationToken)
            .ConfigureAwait(false);
        if (treeError is not null)
        {
            return treeError;
        }

        var affectedRows = await commandExecutor.ExecuteAsync(
                TreeNodeSql.UpdateStatement,
                new Dictionary<string, object?> { ["Id"] = treeNodeId, ["Name"] = request.Name, ["ParentId"] = request.ParentId, ["Version"] = request.Version },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveWriteFailureAsync(
                    treeNodeId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return await queries.FindByIdAsync(
                treeNodeId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<Result<TreeNodeResponse>> DeleteAsync(
        Guid treeNodeId,
            DeleteTreeNodeRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(
            token => DeleteCoreAsync(
                treeNodeId, request,
                actorUserId,
                token),
            cancellationToken);

    private async Task<Result<TreeNodeResponse>> DeleteCoreAsync(
        Guid treeNodeId,
            DeleteTreeNodeRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var existing = await queries.FindByIdAsync(
                treeNodeId,
                cancellationToken)
            .ConfigureAwait(false);
        if (!existing.IsSuccess)
        {
            return existing;
        }


            await AcquireTreeWriteLockAsync(cancellationToken).ConfigureAwait(false);
        var treeChildrenError = await EnsureTreeHasNoChildrenAsync(treeNodeId, cancellationToken).ConfigureAwait(false);
        if (treeChildrenError is not null) return treeChildrenError;

                var affectedRows = await commandExecutor.ExecuteAsync(
                TreeNodeSql.DeleteStatement,
                new Dictionary<string, object?> { ["Id"] = treeNodeId, ["DeletedAtUtc"] = clock.UtcNow, ["DeletedById"] = actorUserId, ["Version"] = request.Version },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveWriteFailureAsync(
                    treeNodeId,
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

    private static Result<TreeNodeResponse>? ValidateWriteRequest(
        string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is { Length: > 64 })
        {
            return ValidationFailure("Name");
        }
        return null;
    }

    private static Result<TreeNodeResponse> ValidationFailure(
        string field) =>
        Result<TreeNodeResponse>.Failure(new Error(
            ValidationErrorCodes.Failed,
            "One or more generated field constraints were not satisfied.",
            ErrorType.Validation,
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [field] = ["The field value is invalid."],
            }, null, new[] { new ValidationViolation(field, "catalog.tree_nodes.invalid_field", new Dictionary<string, object?>()) }));

    private async Task<Result<TreeNodeResponse>> ResolveWriteFailureAsync(
        Guid treeNodeId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<TreeNodeRecord>(
                TreeNodeSql.FindByIdStatement,
                new Dictionary<string, object?> { ["Id"] = treeNodeId },
                cancellationToken)
            .ConfigureAwait(false);
        return record is null ? NotFound() : VersionConflict();
    }

    private static Result<TreeNodeResponse> VersionConflict() =>
        Result<TreeNodeResponse>.Failure(new Error(
            TreeNodeErrorCodes.VersionConflict,
            "The resource was updated concurrently.",
            ErrorType.Conflict));
private async Task AcquireTreeWriteLockAsync(CancellationToken cancellationToken)
{
    // 锁行唯一键使不同节点的写操作共用同一事务互斥；在环检查和结构修改前取得。
    var sql = databaseOptions.Value.Provider == DatabaseProvider.MySql
        ? "INSERT INTO acme_catalog_tree_node_tree_guard (Id, TenantId) VALUES (@Id, @TenantId) ON DUPLICATE KEY UPDATE Id = Id;" : "IF NOT EXISTS (SELECT Id FROM acme_catalog_tree_node_tree_guard WITH (UPDLOCK, HOLDLOCK) WHERE TenantId = @TenantId) INSERT INTO acme_catalog_tree_node_tree_guard (Id, TenantId) VALUES (@Id, @TenantId);";
    await commandExecutor.ExecuteAsync(new SqlStatement("catalog.tree_node.tree_write_lock", sql, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId),
        new Dictionary<string, object?> { ["Id"] = idGenerator.NewId(), ["TenantId"] = currentTenant.Id!.Value }, cancellationToken).ConfigureAwait(false);
}

private async Task<Result<TreeNodeResponse>?> EnsureTreeHasNoChildrenAsync(Guid id, CancellationToken cancellationToken)
{
    var count = await queryExecutor.QuerySingleOrDefaultAsync<long>(new SqlStatement(
        "catalog.tree_node.tree_children",
        "SELECT COUNT(*) FROM acme_catalog_tree_node WHERE ParentId = @Id AND TenantId = @TenantId AND IsDeleted = 0",
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId), new Dictionary<string, object?> { ["Id"] = id, ["TenantId"] = currentTenant.Id!.Value }, cancellationToken).ConfigureAwait(false);
    return count == 0 ? null : Result<TreeNodeResponse>.Failure(new Error(
        "catalog.tree_nodes.parent_has_children", "Remove or move the child nodes first.", ErrorType.Conflict));
}

private async Task<Result<TreeNodeResponse>?> EnsureTreeParentAsync(
    Guid treeNodeId,
    Guid? parentId,
    CancellationToken cancellationToken)
{
    if (parentId is null)
    {
        return null;
    }

    if (parentId.Value == treeNodeId)
    {
        return TreeNodeFeatureErrors.InvalidParent();
    }

    var parent = await queryExecutor
        .QuerySingleOrDefaultAsync<TreeNodeRecord>(
            TreeNodeSql.FindByIdStatement,
            new Dictionary<string, object?> { ["Id"] = parentId.Value, ["TenantId"] = currentTenant.Id!.Value },
            cancellationToken)
        .ConfigureAwait(false);
    if (parent is null)
    {
        return TreeNodeFeatureErrors.InvalidParent();
    }

    var current = parentId.Value;
    for (var depth = 0; depth < 32; depth++)
    {
        var node = await queryExecutor
            .QuerySingleOrDefaultAsync<TreeNodeRecord>(
                TreeNodeSql.FindByIdStatement,
                new Dictionary<string, object?> { ["Id"] = current, ["TenantId"] = currentTenant.Id!.Value },
                cancellationToken)
            .ConfigureAwait(false);
        if (node is null)
        {
            return TreeNodeFeatureErrors.InvalidParent();
        }
        if (node.ParentId is null)
        {
            return null;
        }

        if (node.ParentId.Value == treeNodeId)
        {
            return TreeNodeFeatureErrors.ParentCycle();
        }

        current = node.ParentId.Value;
    }

    return TreeNodeFeatureErrors.ParentCycle();
}

    private static Result<TreeNodeResponse> NotFound() =>
        TreeNodeFeatureErrors.NotFound();
}

internal static class TreeNodeFeatureErrors
{
    internal static Result<TreeNodeResponse> NotFound() =>
        Result<TreeNodeResponse>.Failure(new Error(
            TreeNodeErrorCodes.NotFound,
            "The resource was not found.",
            ErrorType.NotFound));

internal static Result<TreeNodeResponse> InvalidParent() =>
    Result<TreeNodeResponse>.Failure(new Error(
        TreeNodeErrorCodes.InvalidParent,
        "The parent node is missing or outside the current tenant.",
        ErrorType.Validation));

internal static Result<TreeNodeResponse> ParentCycle() =>
    Result<TreeNodeResponse>.Failure(new Error(
        TreeNodeErrorCodes.ParentCycle,
        "The parent assignment would create a cycle.",
        ErrorType.Validation));
}
