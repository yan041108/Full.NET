using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeDocuments;

/// <summary>所有者维护草稿和文档授权；读取同时核对启用目录、知识库及文档成员。</summary>
/// <param name="query">统一查询执行器。</param>
/// <param name="command">统一命令执行器。</param>
/// <param name="transaction">仅覆盖 AI 自有表且失败结果回滚的短事务。</param>
/// <param name="tenant">可信范围，禁止请求覆盖。</param>
/// <param name="database">成对分页 SQL 的提供程序。</param>
/// <param name="clock">UTC 时钟。</param>
/// <param name="ids">应用端 UUID v7 生成器。</param>
internal sealed class AiKnowledgeDocumentService(IQueryExecutor query, ICommandExecutor command,
    ICommandTransaction transaction, ICurrentTenant tenant, IOptions<DatabaseOptions> database, IClock clock, IIdGenerator ids)
{
    private bool IsTenant => AiKnowledgeScope.IsTenant(tenant);

    internal async Task<Result<PagedResult<AiKnowledgeDocumentResponse>>> ListAsync(Guid knowledgeBaseId, Guid actor,
        int page, int pageSize, CancellationToken token)
    {
        if (knowledgeBaseId == Guid.Empty) return NotFound<PagedResult<AiKnowledgeDocumentResponse>>();
        // 仅传入本次 SQL 使用的参数，UUID 类型处理器不接受空标识占位。
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal) { ["KnowledgeBaseId"] = knowledgeBaseId, ["OwnerUserId"] = actor };
        var parent = await query.QuerySingleOrDefaultAsync<AiKnowledgeBaseRecord>(IsTenant ? AiKnowledgeSql.ReadTenant : AiKnowledgeSql.ReadHost,
            AiSqlParameters.Create(("Id", knowledgeBaseId), ("OwnerUserId", actor)), token).ConfigureAwait(false);
        if (parent is not { IsEnabled: true }) return NotFound<PagedResult<AiKnowledgeDocumentResponse>>();
        var offset = ((long)page - 1) * pageSize;
        if (page < 1 || pageSize is < 1 or > 100 || offset > 100_000)
            return Failure<PagedResult<AiKnowledgeDocumentResponse>>(AiKnowledgeErrorCodes.PageInvalid, "Page size must be 1..100 and offset must not exceed 100000.", ErrorType.Validation);
        parameters.Add("Offset", offset);
        parameters.Add("PageSize", pageSize);
        var list = database.Value.Provider switch
        {
            DatabaseProvider.SqlServer => IsTenant ? AiKnowledgeDocumentSql.ListSqlServerTenant : AiKnowledgeDocumentSql.ListSqlServerHost,
            DatabaseProvider.MySql => IsTenant ? AiKnowledgeDocumentSql.ListMySqlTenant : AiKnowledgeDocumentSql.ListMySqlHost,
            _ => throw new InvalidOperationException("Unsupported knowledge database provider."),
        };
        var count = await query.QuerySingleOrDefaultAsync<long>(IsTenant ? AiKnowledgeDocumentSql.CountTenant : AiKnowledgeDocumentSql.CountHost, parameters, token).ConfigureAwait(false);
        var rows = await query.QueryAsync<AiKnowledgeDocumentRecord>(list, parameters, token).ConfigureAwait(false);
        return Result<PagedResult<AiKnowledgeDocumentResponse>>.Success(new(rows.Select(Map).ToArray(), page, pageSize, count));
    }

    internal async Task<Result<AiKnowledgeDocumentResponse>> GetAsync(Guid knowledgeBaseId, Guid id, Guid actor, CancellationToken token)
    {
        if (knowledgeBaseId == Guid.Empty || id == Guid.Empty) return NotFound<AiKnowledgeDocumentResponse>();
        var row = await query.QuerySingleOrDefaultAsync<AiKnowledgeDocumentRecord>(IsTenant ? AiKnowledgeDocumentSql.ReadTenant : AiKnowledgeDocumentSql.ReadHost,
            Parameters(knowledgeBaseId, id, actor), token).ConfigureAwait(false);
        return row is null ? NotFound<AiKnowledgeDocumentResponse>() : Result<AiKnowledgeDocumentResponse>.Success(Map(row));
    }

    internal Task<Result<AiKnowledgeDocumentResponse>> CreateAsync(Guid knowledgeBaseId, Guid actor,
        CreateAiKnowledgeDocumentRequest request, CancellationToken token) => transaction.ExecuteResultAsync(async ct =>
    {
        if (knowledgeBaseId == Guid.Empty) return NotFound<AiKnowledgeDocumentResponse>();
        if (AiKnowledgeDocuments.Validate(request.Title, request.Description) is { } error) return Invalid<AiKnowledgeDocumentResponse>(error);
        var id = ids.NewId();
        var parameters = Parameters(knowledgeBaseId, id, actor);
        parameters.Add("Title", request.Title.Trim());
        parameters.Add("Description", request.Description?.Trim());
        parameters.Add("CreatedAtUtc", clock.UtcNow);
        var changed = await command.ExecuteAsync(IsTenant ? AiKnowledgeDocumentSql.InsertTenant : AiKnowledgeDocumentSql.InsertHost, parameters, ct).ConfigureAwait(false);
        return changed == 1 ? await GetAsync(knowledgeBaseId, id, actor, ct).ConfigureAwait(false) : NotFound<AiKnowledgeDocumentResponse>();
    }, token);

    internal Task<Result<AiKnowledgeDocumentResponse>> UpdateAsync(Guid knowledgeBaseId, Guid id, Guid actor,
        UpdateAiKnowledgeDocumentRequest request, CancellationToken token) => transaction.ExecuteResultAsync(async ct =>
    {
        if (await FindOwnedAsync(knowledgeBaseId, id, actor, ct).ConfigureAwait(false) is null) return NotFound<AiKnowledgeDocumentResponse>();
        if (AiKnowledgeDocuments.Validate(request.Title, request.Description) is { } error) return Invalid<AiKnowledgeDocumentResponse>(error);
        if (request.Version < 1) return Invalid<AiKnowledgeDocumentResponse>("Version must be positive.");
        var parameters = WriteParameters(knowledgeBaseId, id, actor, request.Version);
        parameters.Add("Title", request.Title.Trim());
        parameters.Add("Description", request.Description?.Trim());
        var changed = await command.ExecuteAsync(IsTenant ? AiKnowledgeDocumentSql.UpdateTenant : AiKnowledgeDocumentSql.UpdateHost, parameters, ct).ConfigureAwait(false);
        return changed == 1 ? await GetAsync(knowledgeBaseId, id, actor, ct).ConfigureAwait(false)
            : await WriteFailureAsync<AiKnowledgeDocumentResponse>(knowledgeBaseId, id, actor, ct).ConfigureAwait(false);
    }, token);

    internal Task<Result<bool>> DeleteAsync(Guid knowledgeBaseId, Guid id, Guid actor, int version, CancellationToken token) =>
        transaction.ExecuteResultAsync(async ct =>
        {
            if (await FindOwnedAsync(knowledgeBaseId, id, actor, ct).ConfigureAwait(false) is null) return NotFound<bool>();
            if (version < 1) return Invalid<bool>("Version must be positive.");
            var parameters = WriteParameters(knowledgeBaseId, id, actor, version);
            var changed = await command.ExecuteAsync(IsTenant ? AiKnowledgeDocumentSql.DeleteTenant : AiKnowledgeDocumentSql.DeleteHost, parameters, ct).ConfigureAwait(false);
            if (changed != 1) return await WriteFailureAsync<bool>(knowledgeBaseId, id, actor, ct).ConfigureAwait(false);
            // 先在同一事务写入拒读墓碑，再清理授权；不依赖未来缓存、文件或索引清理。
            await command.ExecuteAsync(IsTenant ? AiKnowledgeDocumentSql.DeleteMembersTenant : AiKnowledgeDocumentSql.DeleteMembersHost, parameters, ct).ConfigureAwait(false);
            return Result<bool>.Success(true);
        }, token);

    internal async Task<Result<AiKnowledgeDocumentMembersResponse>> GetMembersAsync(Guid knowledgeBaseId, Guid id, Guid actor, CancellationToken token)
    {
        var row = await FindOwnedAsync(knowledgeBaseId, id, actor, token).ConfigureAwait(false);
        if (row is null) return NotFound<AiKnowledgeDocumentMembersResponse>();
        var members = await query.QueryAsync<Guid>(IsTenant ? AiKnowledgeDocumentSql.ListMembersTenant : AiKnowledgeDocumentSql.ListMembersHost,
            Parameters(knowledgeBaseId, id, actor), token).ConfigureAwait(false);
        return Result<AiKnowledgeDocumentMembersResponse>.Success(new(id, members.Order().ToArray(), row.Version));
    }

    internal Task<Result<AiKnowledgeDocumentMembersResponse>> SetMembersAsync(Guid knowledgeBaseId, Guid id, Guid actor,
        SetAiKnowledgeDocumentMembersRequest request, CancellationToken token) => transaction.ExecuteResultAsync(async ct =>
    {
        if (await FindOwnedAsync(knowledgeBaseId, id, actor, ct).ConfigureAwait(false) is null) return NotFound<AiKnowledgeDocumentMembersResponse>();
        if (AiKnowledgeMembers.Validate(actor, request.UserIds, request.Version) is { } error)
            return Failure<AiKnowledgeDocumentMembersResponse>(AiKnowledgeErrorCodes.MembersInvalid, error, ErrorType.Validation);
        var parameters = WriteParameters(knowledgeBaseId, id, actor, request.Version);
        var changed = await command.ExecuteAsync(IsTenant ? AiKnowledgeDocumentSql.BumpTenant : AiKnowledgeDocumentSql.BumpHost, parameters, ct).ConfigureAwait(false);
        if (changed != 1) return await WriteFailureAsync<AiKnowledgeDocumentMembersResponse>(knowledgeBaseId, id, actor, ct).ConfigureAwait(false);
        await command.ExecuteAsync(IsTenant ? AiKnowledgeDocumentSql.DeleteMembersTenant : AiKnowledgeDocumentSql.DeleteMembersHost, parameters, ct).ConfigureAwait(false);
        foreach (var user in request.UserIds.Order())
        {
            var insert = Parameters(knowledgeBaseId, id, actor);
            insert.Add("MemberId", ids.NewId());
            insert.Add("UserId", user);
            insert.Add("CreatedAtUtc", clock.UtcNow);
            // INSERT SELECT 再核对当前知识库成员；任一失败必须回滚版本、旧名单及此前插入。
            if (await command.ExecuteAsync(IsTenant ? AiKnowledgeDocumentSql.InsertMemberTenant : AiKnowledgeDocumentSql.InsertMemberHost, insert, ct).ConfigureAwait(false) != 1)
                return Failure<AiKnowledgeDocumentMembersResponse>(AiKnowledgeErrorCodes.DocumentMemberUnavailable,
                    "The requested user is not an explicit member of this knowledge base.", ErrorType.BusinessRule);
        }
        return await GetMembersAsync(knowledgeBaseId, id, actor, ct).ConfigureAwait(false);
    }, token);

    private Task<AiKnowledgeDocumentRecord?> FindOwnedAsync(Guid knowledgeBaseId, Guid id, Guid actor, CancellationToken token) =>
        knowledgeBaseId == Guid.Empty || id == Guid.Empty ? Task.FromResult<AiKnowledgeDocumentRecord?>(null)
        : query.QuerySingleOrDefaultAsync<AiKnowledgeDocumentRecord>(IsTenant ? AiKnowledgeDocumentSql.FindTenant : AiKnowledgeDocumentSql.FindHost,
            Parameters(knowledgeBaseId, id, actor), token);
    private async Task<Result<T>> WriteFailureAsync<T>(Guid knowledgeBaseId, Guid id, Guid actor, CancellationToken token) =>
        await FindOwnedAsync(knowledgeBaseId, id, actor, token).ConfigureAwait(false) is null ? NotFound<T>()
            : Failure<T>(AiKnowledgeErrorCodes.DocumentVersionConflict, "The document was modified by another request.", ErrorType.Conflict);
    private Dictionary<string, object?> WriteParameters(Guid knowledgeBaseId, Guid id, Guid actor, int version)
    {
        var parameters = Parameters(knowledgeBaseId, id, actor);
        parameters.Add("Version", version);
        parameters.Add("UpdatedAtUtc", clock.UtcNow);
        return parameters;
    }
    private static Dictionary<string, object?> Parameters(Guid knowledgeBaseId, Guid id, Guid actor) =>
        new(StringComparer.Ordinal) { ["KnowledgeBaseId"] = knowledgeBaseId, ["DocumentId"] = id, ["OwnerUserId"] = actor };
    private static AiKnowledgeDocumentResponse Map(AiKnowledgeDocumentRecord row) =>
        new(row.Id, row.KnowledgeBaseId, row.Title, row.Description, "draft", row.CreatedAtUtc, row.UpdatedAtUtc, row.Version);
    private static Result<T> Failure<T>(string code, string message, ErrorType type) => Result<T>.Failure(new Error(code, message, type));
    private static Result<T> NotFound<T>() => Failure<T>(AiKnowledgeErrorCodes.DocumentNotFound, "The document or knowledge base was not found.", ErrorType.NotFound);
    private static Result<T> Invalid<T>(string message) => Failure<T>(AiKnowledgeErrorCodes.DocumentInvalid, message, ErrorType.Validation);
}
