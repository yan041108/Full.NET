using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Persistence;
using Full.NET.Modules.Files.Storage;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Files.Features.WorkflowReadCapabilities;

/// <summary>Files自己的元数据和Provider完成跨作用域读取，不进入Workflow事务，也不切换调用者租户。</summary>
internal sealed class WorkflowFileReadCapabilityStore(IQueryExecutor query, ICommandExecutor commands, ICurrentTenant tenant,
    IDataTransactionState transaction, IClock clock, IIdGenerator ids, IIdentityActiveTenantDirectory tenants,
    FileStorageProviderRegistry providers, IOptions<DatabaseOptions> database) : IWorkflowFileReadCapabilityStore
{
    public async Task<Result<WorkflowFileReadCapability>> IssueAsync(WorkflowFileReadCapabilityIssue request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        NoTransaction(); var scope = WorkflowFileReadCapabilityPolicy.Scope(tenant);
        request = request with { ExpiresAtUtc = WorkflowFileReadCapabilityPolicy.Microseconds(request.ExpiresAtUtc) };
        if (!WorkflowFileReadCapabilityPolicy.Valid(request, scope.Id, clock.UtcNow))
            return Failure<WorkflowFileReadCapability>(FilesErrorCodes.ReadCapabilityInvalid, ErrorType.Validation);
        if (!await ActiveAsync(scope.Id, request.RecipientTenantId, cancellationToken).ConfigureAwait(false)) return NotFound<WorkflowFileReadCapability>();
        var parameters = P(("Id", ids.NewId()), ("CallId", request.CallId), ("TenantId", scope.Id), ("TenantScopeKey", scope.Key),
            ("RecipientTenantId", request.RecipientTenantId), ("RecipientScopeKey", WorkflowFileReadCapabilityPolicy.ScopeKey(request.RecipientTenantId)),
            ("DirectionKey", request.DirectionKey), ("FieldKey", request.FieldKey), ("FileId", request.FileId), ("OwnerReferenceId", request.OwnerReferenceId),
            ("ExpiresAtUtc", request.ExpiresAtUtc), ("Now", clock.UtcNow));
        var existing = await query.QuerySingleOrDefaultAsync<WorkflowFileReadCapabilityRecord>(WorkflowFileReadCapabilitySql.FindIssued, parameters, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            try
            {
                if (await commands.ExecuteAsync(scope.Id is null ? WorkflowFileReadCapabilitySql.InsertHost : WorkflowFileReadCapabilitySql.InsertTenant,
                        parameters, cancellationToken).ConfigureAwait(false) != 1) return NotFound<WorkflowFileReadCapability>();
            }
            catch (DataCommandException error) when (error.Kind == DataCommandFailureKind.UniqueConstraint) { }
            existing = await query.QuerySingleOrDefaultAsync<WorkflowFileReadCapabilityRecord>(WorkflowFileReadCapabilitySql.FindIssued, parameters, cancellationToken).ConfigureAwait(false);
        }
        if (existing is null) return NotFound<WorkflowFileReadCapability>();
        if (existing.CallId != request.CallId || existing.IssuerScopeKey != scope.Key || existing.RecipientTenantId != request.RecipientTenantId ||
            existing.DirectionKey != request.DirectionKey || existing.FieldKey != request.FieldKey || existing.SourceFileId != request.FileId ||
            existing.OwnerReferenceId != request.OwnerReferenceId || existing.ExpiresAtUtc != request.ExpiresAtUtc)
            return Failure<WorkflowFileReadCapability>(FilesErrorCodes.ReadCapabilityConflict, ErrorType.Conflict);
        var ready = await FindAsync(existing.Id, request.CallId, true, cancellationToken).ConfigureAwait(false);
        return ready is null ? NotFound<WorkflowFileReadCapability>() : Result<WorkflowFileReadCapability>.Success(WorkflowFileReadCapabilityPolicy.Describe(ready));
    }

    public async Task<WorkflowFileReadCapability?> DescribeAsync(Guid capabilityId, Guid callId, CancellationToken cancellationToken = default)
    {
        var row = await FindAsync(capabilityId, callId, false, cancellationToken).ConfigureAwait(false);
        return row is null ? null : WorkflowFileReadCapabilityPolicy.Describe(row);
    }

    public async Task<Result<WorkflowFileReadContent>> OpenAsync(Guid capabilityId, Guid callId, CancellationToken cancellationToken = default)
    {
        var row = await FindAsync(capabilityId, callId, false, cancellationToken).ConfigureAwait(false);
        if (row?.ProviderKey is not { } provider || row.StorageKey is not { } key) return NotFound<WorkflowFileReadContent>();
        try
        {
            var stream = await providers.Resolve(provider).OpenReadAsync(key, cancellationToken).ConfigureAwait(false);
            return Result<WorkflowFileReadContent>.Success(new(stream, WorkflowFileReadCapabilityPolicy.Describe(row)));
        }
        catch (FileNotFoundException) { return NotFound<WorkflowFileReadContent>(); }
    }

    public async Task<WorkflowIssuedFileReference?> ResolveIssuedAsync(Guid capabilityId, Guid callId, CancellationToken cancellationToken = default)
    {
        var row = await FindAsync(capabilityId, callId, true, cancellationToken).ConfigureAwait(false);
        return row is null ? null : new(row.SourceFileId, row.OwnerReferenceId, WorkflowFileReadCapabilityPolicy.Describe(row));
    }

    public async Task<bool> ReleaseIssuedAsync(Guid callId, CancellationToken cancellationToken = default)
    {
        NoTransaction(); var scope = WorkflowFileReadCapabilityPolicy.Scope(tenant); if (callId == Guid.Empty) return true;
        var parameters = P(("CallId", callId), ("TenantScopeKey", scope.Key), ("Now", clock.UtcNow));
        var selected = await query.QueryAsync<Guid>(database.Value.Provider == DatabaseProvider.SqlServer
            ? WorkflowFileReadCapabilitySql.ListToReleaseSqlServer : WorkflowFileReadCapabilitySql.ListToReleaseMySql, parameters, cancellationToken).ConfigureAwait(false);
        if (selected.Count == 0) return true;
        parameters["Ids"] = selected.ToArray();
        await commands.ExecuteAsync(WorkflowFileReadCapabilitySql.RevokeIssued, parameters, cancellationToken).ConfigureAwait(false);
        return selected.Count < 25;
    }

    /// <summary>Host Worker仅做有界过期元数据收敛；读权限始终直接比较期限，不依赖后台及时运行。</summary>
    internal async Task<int> ExpireAsync(CancellationToken token)
    {
        NoTransaction(); if (!tenant.IsHost) throw new TenantContextMissingException("files.host_context_required");
        var parameters = P(("Now", clock.UtcNow));
        var selected = await query.QueryAsync<Guid>(database.Value.Provider == DatabaseProvider.SqlServer
            ? WorkflowFileReadCapabilitySql.ListExpiredSqlServer : WorkflowFileReadCapabilitySql.ListExpiredMySql, parameters, token).ConfigureAwait(false);
        if (selected.Count == 0) return 0;
        parameters["Ids"] = selected.ToArray();
        return await commands.ExecuteAsync(WorkflowFileReadCapabilitySql.RevokeExpired, parameters, token).ConfigureAwait(false);
    }

    private async Task<WorkflowFileReadCapabilityRecord?> FindAsync(Guid id, Guid callId, bool issuer, CancellationToken token)
    {
        NoTransaction(); var scope = WorkflowFileReadCapabilityPolicy.Scope(tenant); if (id == Guid.Empty || callId == Guid.Empty) return null;
        var sql = (database.Value.Provider == DatabaseProvider.SqlServer, issuer) switch
        {
            (true, true) => WorkflowFileReadCapabilitySql.FindOwnReadySqlServer, (true, false) => WorkflowFileReadCapabilitySql.FindReceivedReadySqlServer,
            (false, true) => WorkflowFileReadCapabilitySql.FindOwnReady, _ => WorkflowFileReadCapabilitySql.FindReceivedReady
        };
        var row = await query.QuerySingleOrDefaultAsync<WorkflowFileReadCapabilityRecord>(sql,
            P(("Id", id), ("CallId", callId), ("TenantScopeKey", scope.Key), ("Now", clock.UtcNow)), token).ConfigureAwait(false);
        return row is not null && row.Id == id && row.CallId == callId && (issuer ? row.IssuerScopeKey : row.RecipientScopeKey) == scope.Key &&
            WorkflowFileReadCapabilityPolicy.Valid(row, clock.UtcNow) && await ActiveAsync(row.IssuerTenantId, row.RecipientTenantId, token).ConfigureAwait(false) ? row : null;
    }
    private async Task<bool> ActiveAsync(Guid? issuer, Guid? recipient, CancellationToken token) =>
        (issuer is null || await tenants.IsActiveTenantAsync(issuer.Value, token).ConfigureAwait(false)) &&
        (recipient is null || await tenants.IsActiveTenantAsync(recipient.Value, token).ConfigureAwait(false));
    private void NoTransaction() { if (transaction.HasTransaction) throw new InvalidOperationException("Workflow file capabilities cannot borrow a business transaction."); }
    private static Dictionary<string, object?> P(params (string Name, object? Value)[] values) => values.ToDictionary(item => item.Name, item => item.Value, StringComparer.Ordinal);
    private static Result<T> NotFound<T>() => Failure<T>(FilesErrorCodes.ReadCapabilityNotFound, ErrorType.NotFound);
    private static Result<T> Failure<T>(string code, ErrorType type) => Result<T>.Failure(new(code, "The workflow file capability is unavailable.", type));
}
