using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Serialization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Features.IncrementalKnowledgeIndexing;

/// <summary>所有者显式管理有界收费委托；原登录冻结于 AI 表，不允许客户端传入主体或模型。</summary>
internal sealed class AiKnowledgeIndexGrantService(AiKnowledgeIndexGrantStore grants, IQueryExecutor query, ICommandExecutor commands,
    ICommandTransaction transaction, IDataTransactionState transactions, ICurrentTenant tenant, IClock clock, IIdGenerator ids,
    IBackgroundSessionAuthorization authorization, IOptions<DatabaseOptions> database)
{
    internal async Task<Result<AiKnowledgeIndexGrantResponse>> CreateAsync(Guid knowledgeId, SessionBindingSnapshot original,
        CreateAiKnowledgeIndexGrantRequest request, CancellationToken token)
    {
        Outside(token);
        // 两库统一到微秒，避免 MySQL 保存成功后因第七位小数导致幂等回读误报冲突。
        request = request with { ExpiresAtUtc = NormalizeExpiry(request.ExpiresAtUtc) };
        if (!grants.Enabled || !AiKnowledgeIndexGrantPolicy.Valid(request, clock.UtcNow)) return Failure<AiKnowledgeIndexGrantResponse>(ErrorType.Validation);
        if (!await AuthorizedAsync(original, AiKnowledgeIndexGrantPermissions.Create, true, token).ConfigureAwait(false)) return Failure<AiKnowledgeIndexGrantResponse>(ErrorType.Forbidden);
        var p = Parameters(knowledgeId, original.UserId); p["ClientRequestId"] = request.ClientRequestId;
        var sessionJson = JsonSerializer.Serialize(original, AiJsonSerializerContext.Default.SessionBindingSnapshot);
        var existing = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexGrantRecord>(grants.IsTenant ? AiKnowledgeIndexGrantSql.ClientTenant : AiKnowledgeIndexGrantSql.ClientHost, p, token).ConfigureAwait(false);
        Outside(token);
        if (existing is not null) return Same(original) && Matches(existing, request, original, knowledgeId, sessionJson)
            ? Result<AiKnowledgeIndexGrantResponse>.Success(Map(existing)) : Failure<AiKnowledgeIndexGrantResponse>(ErrorType.Conflict);
        var knowledge = await KnowledgeAsync(knowledgeId, original.UserId, token).ConfigureAwait(false);
        if (knowledge is not { IsEnabled: true, EmbeddingModelConfigId: not null, EmbeddingModelVersion: > 0 }
            || knowledge.Version != request.KnowledgeBaseVersion || knowledge.TenantId != original.TenantId) return Failure<AiKnowledgeIndexGrantResponse>(ErrorType.NotFound);
        if (!await AuthorizedAsync(original, AiKnowledgeIndexGrantPermissions.Create, true, token).ConfigureAwait(false)) return Failure<AiKnowledgeIndexGrantResponse>(ErrorType.Forbidden);
        p["GrantId"] = ids.NewId(); p["KnowledgeBaseVersion"] = knowledge.Version; p["ModelConfigId"] = knowledge.EmbeddingModelConfigId;
        p["ModelConfigVersion"] = knowledge.EmbeddingModelVersion; p["SessionJson"] = sessionJson; p["ExpiresAtUtc"] = request.ExpiresAtUtc;
        p["DocumentLimit"] = request.DocumentLimit; p["InputTokenLimitPerDocument"] = request.InputTokenLimitPerDocument; p["Now"] = clock.UtcNow;
        try
        {
            var written = await transaction.ExecuteAsync(async ct =>
            {
                ct.ThrowIfCancellationRequested();
                if (!grants.Enabled || !Same(original) || !AiKnowledgeIndexGrantPolicy.Valid(request, clock.UtcNow)) return false;
                var count = await commands.ExecuteAsync(grants.IsTenant ? AiKnowledgeIndexGrantSql.InsertTenant : AiKnowledgeIndexGrantSql.InsertHost, p, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                if (count == 1 && (!Same(original) || !grants.Enabled)) throw new InvalidOperationException("Knowledge index grant scope changed during creation.");
                return count == 1;
            }, token).ConfigureAwait(false);
            if (!written) return Failure<AiKnowledgeIndexGrantResponse>(ErrorType.Conflict);
        }
        catch (DataCommandException error) when (error.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            // 唯一竞争仅恢复完全相同原会话及绝对期限的请求，不覆盖或延长旧授权。
        }
        if (!await AuthorizedAsync(original, AiKnowledgeIndexGrantPermissions.Create, true, token).ConfigureAwait(false)) return Failure<AiKnowledgeIndexGrantResponse>(ErrorType.Forbidden);
        existing = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexGrantRecord>(grants.IsTenant ? AiKnowledgeIndexGrantSql.ClientTenant : AiKnowledgeIndexGrantSql.ClientHost, p, token).ConfigureAwait(false);
        Outside(token);
        return existing is not null && Same(original) && Matches(existing, request, original, knowledgeId, sessionJson)
            ? Result<AiKnowledgeIndexGrantResponse>.Success(Map(existing)) : Failure<AiKnowledgeIndexGrantResponse>(ErrorType.Conflict);
    }

    internal async Task<Result<AiKnowledgeIndexGrantResponse[]>> ListAsync(Guid knowledgeId, Guid? afterId,
        SessionBindingSnapshot original, CancellationToken token)
    {
        Outside(token);
        if (knowledgeId == Guid.Empty || afterId == Guid.Empty) return Failure<AiKnowledgeIndexGrantResponse[]>(ErrorType.Validation);
        if (!await AuthorizedAsync(original, AiKnowledgeIndexGrantPermissions.Read, false, token).ConfigureAwait(false)) return Failure<AiKnowledgeIndexGrantResponse[]>(ErrorType.Forbidden);
        var p = Parameters(knowledgeId, original.UserId); p["HasCursor"] = afterId.HasValue ? 1 : 0;
        p["AfterId"] = afterId ?? new Guid("00000000-0000-0000-0000-000000000001");
        var sql = database.Value.Provider == DatabaseProvider.SqlServer
            ? grants.IsTenant ? AiKnowledgeIndexGrantSql.ListSqlServerTenant : AiKnowledgeIndexGrantSql.ListSqlServerHost
            : database.Value.Provider == DatabaseProvider.MySql
                ? grants.IsTenant ? AiKnowledgeIndexGrantSql.ListMySqlTenant : AiKnowledgeIndexGrantSql.ListMySqlHost
                : throw new InvalidOperationException("Unsupported knowledge grant provider.");
        var rows = await query.QueryAsync<AiKnowledgeIndexGrantRecord>(sql, p, token).ConfigureAwait(false);
        if (!await AuthorizedAsync(original, AiKnowledgeIndexGrantPermissions.Read, false, token).ConfigureAwait(false)) return Failure<AiKnowledgeIndexGrantResponse[]>(ErrorType.Forbidden);
        Outside(token);
        if (rows.Count > 32 || rows.Any(row => row.OwnerUserId != original.UserId || row.TenantId != original.TenantId || row.KnowledgeBaseId != knowledgeId))
            throw new InvalidDataException("Invalid knowledge grant list.");
        return Result<AiKnowledgeIndexGrantResponse[]>.Success(rows.Select(Map).ToArray());
    }

    internal async Task<Result<AiKnowledgeIndexGrantResponse>> RevokeAsync(Guid knowledgeId, Guid id,
        SessionBindingSnapshot original, RevokeAiKnowledgeIndexGrantRequest request, CancellationToken token)
    {
        Outside(token);
        if (id == Guid.Empty || request.Version is < 1 or int.MaxValue) return Failure<AiKnowledgeIndexGrantResponse>(ErrorType.Validation);
        // 撤销允许使用本人当前登录；不能借此接管旧授权或创建替代收费任务。
        if (!await AuthorizedAsync(original, AiKnowledgeIndexGrantPermissions.Revoke, false, token).ConfigureAwait(false)) return Failure<AiKnowledgeIndexGrantResponse>(ErrorType.Forbidden);
        var p = Parameters(knowledgeId, original.UserId); p["GrantId"] = id; p["GrantVersion"] = request.Version;
        var affected = await commands.ExecuteAsync(grants.IsTenant ? AiKnowledgeIndexGrantSql.RevokeTenant : AiKnowledgeIndexGrantSql.RevokeHost, p, token).ConfigureAwait(false);
        if (affected != 1) return Failure<AiKnowledgeIndexGrantResponse>(ErrorType.Conflict);
        if (!await AuthorizedAsync(original, AiKnowledgeIndexGrantPermissions.Revoke, false, token).ConfigureAwait(false)) return Failure<AiKnowledgeIndexGrantResponse>(ErrorType.Forbidden);
        var row = await grants.LoadAsync(id, token).ConfigureAwait(false); Outside(token);
        return row is not null && row.OwnerUserId == original.UserId && row.KnowledgeBaseId == knowledgeId && row.TenantId == original.TenantId
            && row.IsRevoked && Same(original) ? Result<AiKnowledgeIndexGrantResponse>.Success(Map(row)) : Failure<AiKnowledgeIndexGrantResponse>(ErrorType.NotFound);
    }

    private async Task<bool> AuthorizedAsync(SessionBindingSnapshot original, string permission, bool paid, CancellationToken token)
    {
        var permissions = paid ? Features.ManageKnowledgeIndexing.AiKnowledgeIndexEnqueueService.Permissions.Append(permission).ToArray()
            : new[] { AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read, permission };
        {
            Outside(token); if (!Same(original)) return false;
            var actor = await authorization.AuthorizeAllAsync(original, permissions, token).ConfigureAwait(false);
            Outside(token);
            if (!Same(original) || actor?.UserId != original.UserId || actor.TenantId != original.TenantId || actor.SessionId != original.SessionId) return false;
        }
        return true;
    }
    private Task<AiKnowledgeBaseRecord?> KnowledgeAsync(Guid id, Guid actor, CancellationToken token) => query.QuerySingleOrDefaultAsync<AiKnowledgeBaseRecord>(
        grants.IsTenant ? AiKnowledgeSql.FindTenant : AiKnowledgeSql.FindHost, new Dictionary<string, object?> { ["Id"] = id, ["OwnerUserId"] = actor }, token);
    private static bool Matches(AiKnowledgeIndexGrantRecord row, CreateAiKnowledgeIndexGrantRequest request, SessionBindingSnapshot original, Guid knowledgeId, string json) =>
        row.OwnerUserId == original.UserId && row.TenantId == original.TenantId && row.KnowledgeBaseId == knowledgeId && row.SessionJson == json
        && row.ClientRequestId == request.ClientRequestId && row.KnowledgeBaseVersion == request.KnowledgeBaseVersion
        && row.ExpiresAtUtc == request.ExpiresAtUtc && row.DocumentLimit == request.DocumentLimit && row.InputTokenLimitPerDocument == request.InputTokenLimitPerDocument;
    private bool Same(SessionBindingSnapshot original) => original.UserId != Guid.Empty && original.SessionId != Guid.Empty && original.TenantId == tenant.Id
        && original.TenantId != Guid.Empty && grants.IsTenant == original.TenantId.HasValue;
    private static Dictionary<string, object?> Parameters(Guid knowledgeId, Guid actor) => new() { ["KnowledgeBaseId"] = knowledgeId, ["OwnerUserId"] = actor };
    internal static DateTimeOffset NormalizeExpiry(DateTimeOffset value) => new(value.UtcTicks - value.UtcTicks % 10, TimeSpan.Zero);
    internal static AiKnowledgeIndexGrantResponse Map(AiKnowledgeIndexGrantRecord row) => new(row.Id, row.KnowledgeBaseId, row.KnowledgeBaseVersion,
        row.ExpiresAtUtc, row.DocumentLimit, row.AdmittedDocuments, row.InputTokenLimitPerDocument, row.IsRevoked, row.Version);
    private void Outside(CancellationToken token) { token.ThrowIfCancellationRequested(); if (transactions.HasTransaction) throw new InvalidOperationException("Knowledge grant management cannot join an ambient transaction."); }
    private static Result<T> Failure<T>(ErrorType type) => Result<T>.Failure(new(type == ErrorType.Forbidden ? "authorization.permission_denied"
        : type == ErrorType.Conflict ? AiKnowledgeErrorCodes.IndexConflict : type == ErrorType.NotFound ? AiKnowledgeErrorCodes.NotFound : AiKnowledgeErrorCodes.InputInvalid,
        "The paid incremental grant is unavailable or invalid.", type));
}
