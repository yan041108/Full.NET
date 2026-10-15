using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Features.IncrementalKnowledgeIndexing;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeVersions;

/// <summary>所有导入写入只覆盖 AI 自有表；提交阶段不调用 Files 或其他模块。</summary>
internal sealed class AiKnowledgeImportStore(IQueryExecutor query, ICommandExecutor command,
    ICommandTransaction transaction, ICurrentTenant tenant, IClock clock, IIdGenerator ids, IOptions<DatabaseOptions> database, IOutboxWriter outbox)
{
    internal bool IsTenant => AiKnowledgeScope.IsTenant(tenant);
    internal DateTimeOffset Now => clock.UtcNow;
    internal Guid NewId() => ids.NewId();
    internal DatabaseProvider Provider => database.Value.Provider;
    internal Dictionary<string, object?> Parameters(Guid knowledgeBaseId, Guid documentId, Guid actor) =>
        new(StringComparer.Ordinal) { ["KnowledgeBaseId"] = knowledgeBaseId, ["DocumentId"] = documentId, ["OwnerUserId"] = actor };

    internal Task<AiKnowledgeDocumentRecord?> OwnedDocumentAsync(Guid knowledgeBaseId, Guid documentId, Guid actor, bool includeDeleted, CancellationToken token) =>
        knowledgeBaseId == Guid.Empty || documentId == Guid.Empty ? Task.FromResult<AiKnowledgeDocumentRecord?>(null)
        : query.QuerySingleOrDefaultAsync<AiKnowledgeDocumentRecord>(includeDeleted
            ? IsTenant ? AiKnowledgeImportSql.FindAnyDocumentTenant : AiKnowledgeImportSql.FindAnyDocumentHost
            : IsTenant ? AiKnowledgeDocumentSql.FindTenant : AiKnowledgeDocumentSql.FindHost,
            Parameters(knowledgeBaseId, documentId, actor), token);

    internal Task<AiKnowledgeImportRecord?> FindAsync(Dictionary<string, object?> parameters, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<AiKnowledgeImportRecord>(IsTenant ? AiKnowledgeImportSql.FindTenant : AiKnowledgeImportSql.FindHost, parameters, token);

    internal async Task<Result<AiKnowledgeImportRecord>> StartAsync(Guid knowledgeBaseId, Guid documentId, Guid actor,
        int version, Guid requestKey, string name, string type, AiKnowledgeUploadBuffer payload, CancellationToken token)
    {
        var parameters = Parameters(knowledgeBaseId, documentId, actor);
        parameters.Add("RequestKey", requestKey);
        var existing = await FindKeyAsync(parameters, token).ConfigureAwait(false);
        if (existing is not null) return Match(existing, version, name, type, payload);
        var document = await OwnedDocumentAsync(knowledgeBaseId, documentId, actor, false, token).ConfigureAwait(false);
        if (document is null) return NotFound<AiKnowledgeImportRecord>();
        if (document.Version != version) return Conflict<AiKnowledgeImportRecord>();
        parameters.Add("OperationId", ids.NewId());
        parameters.Add("VersionId", ids.NewId());
        parameters.Add("Version", version);
        parameters.Add("OriginalFileName", name);
        parameters.Add("ContentType", type);
        parameters.Add("ContentHash", payload.Hash);
        parameters.Add("SizeBytes", payload.Size);
        parameters.Add("Now", clock.UtcNow);
        try
        {
            if (await command.ExecuteAsync(IsTenant ? AiKnowledgeImportSql.InsertTenant : AiKnowledgeImportSql.InsertHost, parameters, token).ConfigureAwait(false) != 1)
                return Conflict<AiKnowledgeImportRecord>();
        }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            // 唯一键竞争只复用已经持久化且完全匹配的请求；其他数据库失败仍向上抛出。
            existing = await FindKeyAsync(parameters, token).ConfigureAwait(false);
            if (existing is null) throw;
            return Match(existing, version, name, type, payload);
        }
        return Result<AiKnowledgeImportRecord>.Success((await FindKeyAsync(parameters, token).ConfigureAwait(false))!);
    }

    private Task<AiKnowledgeImportRecord?> FindKeyAsync(Dictionary<string, object?> parameters, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<AiKnowledgeImportRecord>(IsTenant ? AiKnowledgeImportSql.FindKeyTenant : AiKnowledgeImportSql.FindKeyHost, parameters, token);
    private static Result<AiKnowledgeImportRecord> Match(AiKnowledgeImportRecord row, int version, string name, string type, AiKnowledgeUploadBuffer payload) =>
        row.ExpectedDocumentVersion == version && row.OriginalFileName == name && row.ContentType == type
            && row.ContentHash == payload.Hash && row.SizeBytes == payload.Size
            ? Result<AiKnowledgeImportRecord>.Success(row)
            : Failure<AiKnowledgeImportRecord>("ai.knowledge.import_conflict", "The retry key belongs to a different request.", ErrorType.Conflict);

    internal async Task<bool> AcquireAsync(Dictionary<string, object?> parameters, CancellationToken token)
    {
        parameters["LeaseToken"] = ids.NewId();
        parameters["Now"] = clock.UtcNow;
        parameters["LeaseExpiresAtUtc"] = clock.UtcNow.AddMinutes(5);
        return await command.ExecuteAsync(IsTenant ? AiKnowledgeImportSql.AcquireTenant : AiKnowledgeImportSql.AcquireHost, parameters, token).ConfigureAwait(false) == 1;
    }

    internal async Task<bool> SaveFileAsync(Dictionary<string, object?> parameters, Guid fileId, CancellationToken token)
    {
        parameters["FileId"] = fileId;
        parameters["Now"] = clock.UtcNow;
        return await command.ExecuteAsync(IsTenant ? AiKnowledgeImportSql.SaveFileTenant : AiKnowledgeImportSql.SaveFileHost, parameters, token).ConfigureAwait(false) == 1;
    }

    internal async Task<bool> StateAsync(Dictionary<string, object?> parameters, string state, string? failure, CancellationToken token)
    {
        parameters["StateKey"] = state;
        parameters["FailureCode"] = failure;
        parameters["Now"] = clock.UtcNow;
        if (state != "completed")
            return await command.ExecuteAsync(IsTenant ? AiKnowledgeImportSql.SetStateTenant : AiKnowledgeImportSql.SetStateHost, parameters, token).ConfigureAwait(false) == 1;
        return await transaction.ExecuteAsync(async cancellation =>
        {
            if (await command.ExecuteAsync(IsTenant ? AiKnowledgeImportSql.SetStateTenant : AiKnowledgeImportSql.SetStateHost, parameters, cancellation).ConfigureAwait(false) != 1)
                return false;
            var operation = await FindAsync(parameters, cancellation).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Confirmed import must remain readable in its transaction.");
            // 只有确认前态真正完成才产生业务事件；Outbox 失败回滚完成状态，重试可安全补发。
            await outbox.AddAsync(AiKnowledgeSourceConfirmed.EventType, 1,
                new AiKnowledgeSourceConfirmed((Guid)parameters["KnowledgeBaseId"]!, operation.DocumentId, operation.VersionId, tenant.Id), cancellation).ConfigureAwait(false);
            return true;
        }, token).ConfigureAwait(false);
    }

    internal async Task ReleaseLeaseAsync(Dictionary<string, object?> parameters, CancellationToken token)
    {
        _ = await command.ExecuteAsync(IsTenant ? AiKnowledgeImportSql.ReleaseLeaseTenant : AiKnowledgeImportSql.ReleaseLeaseHost, parameters, token).ConfigureAwait(false);
    }

    internal Task<Result<bool>> PublishAsync(Dictionary<string, object?> parameters, CancellationToken token) =>
        transaction.ExecuteResultAsync(async ct =>
        {
            var operation = await FindAsync(parameters, ct).ConfigureAwait(false);
            if (operation is null || operation.StateKey != "file_uploaded" || operation.LeaseToken != (Guid)parameters["LeaseToken"]!
                || operation.LeaseExpiresAtUtc <= clock.UtcNow) return Conflict<bool>();
            parameters["Version"] = operation.ExpectedDocumentVersion;
            parameters["UpdatedAtUtc"] = clock.UtcNow;
            if (await command.ExecuteAsync(IsTenant ? AiKnowledgeDocumentSql.BumpTenant : AiKnowledgeDocumentSql.BumpHost, parameters, ct).ConfigureAwait(false) != 1)
                return await OwnedDocumentAsync((Guid)parameters["KnowledgeBaseId"]!, (Guid)parameters["DocumentId"]!, (Guid)parameters["OwnerUserId"]!, false, ct).ConfigureAwait(false) is null
                    ? NotFound<bool>() : Conflict<bool>();
            // 文档行已由版本更新持有写锁，文件序号在同一事务内串行分配。
            var count = await query.QuerySingleOrDefaultAsync<long>(IsTenant ? AiKnowledgeVersionSql.CountOwnedTenant : AiKnowledgeVersionSql.CountOwnedHost, parameters, ct).ConfigureAwait(false);
            parameters["VersionNumber"] = checked((int)count + 1);
            parameters["Now"] = clock.UtcNow;
            if (await command.ExecuteAsync(IsTenant ? AiKnowledgeVersionSql.InsertTenant : AiKnowledgeVersionSql.InsertHost, parameters, ct).ConfigureAwait(false) != 1)
                return Conflict<bool>();
            if (!await StateAsync(parameters, "pending_confirmation", null, ct).ConfigureAwait(false)) return Conflict<bool>();
            return Result<bool>.Success(true);
        }, token);

    internal Task<IReadOnlyList<AiKnowledgeImportRecord>> CleanupRowsAsync(Dictionary<string, object?> parameters, CancellationToken token)
    {
        parameters["PageSize"] = 25;
        var sql = Provider == DatabaseProvider.SqlServer
            ? IsTenant ? AiKnowledgeImportSql.ListCleanupSqlServerTenant : AiKnowledgeImportSql.ListCleanupSqlServerHost
            : IsTenant ? AiKnowledgeImportSql.ListCleanupMySqlTenant : AiKnowledgeImportSql.ListCleanupMySqlHost;
        return query.QueryAsync<AiKnowledgeImportRecord>(sql, parameters, token);
    }

    internal static AiKnowledgeImportResponse Map(AiKnowledgeImportRecord row) =>
        new(row.Id, row.DocumentId, row.VersionId, row.StateKey, row.FailureCode, row.CreatedAtUtc, row.UpdatedAtUtc);
    internal static Result<T> NotFound<T>() => Failure<T>(AiKnowledgeErrorCodes.DocumentNotFound, "The document or operation was not found.", ErrorType.NotFound);
    internal static Result<T> Conflict<T>() => Failure<T>(AiKnowledgeErrorCodes.DocumentVersionConflict, "The document or operation was modified.", ErrorType.Conflict);
    internal static Result<T> Failure<T>(string code, string message, ErrorType type) => Result<T>.Failure(new Error(code, message, type));
}
