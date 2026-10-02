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

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeBases;

/// <summary>私有目录与精确模型审批；所有读写都同时约束可信租户与用户。</summary>
/// <param name="query">统一查询执行器。</param>
/// <param name="command">统一命令执行器。</param>
/// <param name="transaction">失败结果可回滚的短事务。</param>
/// <param name="tenant">可信租户上下文。</param>
/// <param name="database">双库分页选项。</param>
/// <param name="clock">UTC 时钟。</param>
/// <param name="ids">应用端 UUID v7 生成器。</param>
internal sealed class AiKnowledgeBaseService(IQueryExecutor query, ICommandExecutor command,
    ICommandTransaction transaction, ICurrentTenant tenant, IOptions<DatabaseOptions> database, IClock clock, IIdGenerator ids)
{
    internal async Task<Result<PagedResult<AiKnowledgeBaseResponse>>> ListAsync(Guid owner, int page, int pageSize, CancellationToken token)
    {
        var isTenant = AiKnowledgeScope.IsTenant(tenant);
        // 先用长整型计算并限制深分页，避免溢出和无界扫描。
        var offset = ((long)page - 1) * pageSize;
        if (page < 1 || pageSize is < 1 or > 100 || offset > 100_000)
            return Failure<PagedResult<AiKnowledgeBaseResponse>>(AiKnowledgeErrorCodes.PageInvalid, "Page size must be 1..100 and offset must not exceed 100000.", ErrorType.Validation);
        var parameters = AiSqlParameters.Create(("OwnerUserId", owner), ("Offset", offset), ("PageSize", pageSize));
        var list = database.Value.Provider switch
        {
            DatabaseProvider.SqlServer => isTenant ? AiKnowledgeSql.ListSqlServerTenant : AiKnowledgeSql.ListSqlServerHost,
            DatabaseProvider.MySql => isTenant ? AiKnowledgeSql.ListMySqlTenant : AiKnowledgeSql.ListMySqlHost,
            _ => throw new InvalidOperationException("Unsupported knowledge database provider."),
        };
        var total = await query.QuerySingleOrDefaultAsync<long>(isTenant ? AiKnowledgeSql.CountTenant : AiKnowledgeSql.CountHost, parameters, token).ConfigureAwait(false);
        var rows = await query.QueryAsync<AiKnowledgeBaseRecord>(list, parameters, token).ConfigureAwait(false);
        return Result<PagedResult<AiKnowledgeBaseResponse>>.Success(new(rows.Select(Map).ToArray(), page, pageSize, total));
    }

    internal async Task<Result<AiKnowledgeBaseResponse>> GetAsync(Guid id, Guid owner, CancellationToken token)
    {
        var row = await FindAsync(id, owner, token).ConfigureAwait(false);
        return row is null ? NotFound() : Result<AiKnowledgeBaseResponse>.Success(Map(row));
    }

    internal Task<Result<AiKnowledgeBaseResponse>> CreateAsync(Guid owner, CreateAiKnowledgeBaseRequest request, CancellationToken token) =>
        transaction.ExecuteResultAsync(async ct =>
        {
            var isTenant = AiKnowledgeScope.IsTenant(tenant);
            if (ValidateMetadata(request.Name, request.Description) is { } error) return Invalid(error);
            var id = ids.NewId();
            await command.ExecuteAsync(isTenant ? AiKnowledgeSql.InsertTenant : AiKnowledgeSql.InsertHost,
                AiSqlParameters.Create(("Id", id), ("OwnerUserId", owner), ("Name", request.Name.Trim()),
                    ("Description", request.Description?.Trim()), ("CreatedAtUtc", clock.UtcNow)), ct).ConfigureAwait(false);
            return await GetAsync(id, owner, ct).ConfigureAwait(false);
        }, token);

    internal Task<Result<AiKnowledgeBaseResponse>> UpdateAsync(Guid id, Guid owner, UpdateAiKnowledgeBaseRequest request, CancellationToken token) =>
        transaction.ExecuteResultAsync(async ct =>
        {
            // 不存在或不归属的资源统一返回 404，不暴露别人的版本或目录。
            if (await FindAsync(id, owner, ct).ConfigureAwait(false) is null) return NotFound();
            if (ValidateMetadata(request.Name, request.Description) is { } error) return Invalid(error);
            if (request.Version < 1) return Invalid("Version must be positive.");
            var parameters = AiSqlParameters.Create(("Id", id), ("OwnerUserId", owner), ("Version", request.Version),
                ("Name", request.Name.Trim()), ("Description", request.Description?.Trim()),
                ("IsEnabled", request.IsEnabled), ("UpdatedAtUtc", clock.UtcNow));
            var affected = await command.ExecuteAsync(AiKnowledgeScope.IsTenant(tenant) ? AiKnowledgeSql.UpdateTenant : AiKnowledgeSql.UpdateHost, parameters, ct).ConfigureAwait(false);
            return affected == 1 ? await GetAsync(id, owner, ct).ConfigureAwait(false) : Conflict();
        }, token);

    internal Task<Result<AiKnowledgeBaseResponse>> UpdatePolicyAsync(Guid id, Guid owner, UpdateAiKnowledgePolicyRequest request, CancellationToken token) =>
        transaction.ExecuteResultAsync(async ct =>
        {
            if (await FindAsync(id, owner, ct).ConfigureAwait(false) is null) return NotFound();
            if (request.Version < 1) return Invalid("Version must be positive.");
            if (AiKnowledgePolicy.Validate(request.DataClassification, request.EmbeddingModelConfigId, request.EmbeddingModelVersion,
                    request.GenerationModelConfigId, request.GenerationModelVersion) is { } error) return Invalid(error);
            if (!await IsAvailableModelAsync(request.EmbeddingModelConfigId, request.EmbeddingModelVersion, ct).ConfigureAwait(false)
                || !await IsAvailableModelAsync(request.GenerationModelConfigId, request.GenerationModelVersion, ct).ConfigureAwait(false))
                return Failure<AiKnowledgeBaseResponse>(AiKnowledgeErrorCodes.ModelUnavailable, "The requested model configuration version is unavailable.", ErrorType.BusinessRule);
            var parameters = AiSqlParameters.Create(("Id", id), ("OwnerUserId", owner), ("Version", request.Version),
                ("DataClassification", request.DataClassification), ("EmbeddingModelConfigId", request.EmbeddingModelConfigId),
                ("EmbeddingModelVersion", request.EmbeddingModelVersion), ("GenerationModelConfigId", request.GenerationModelConfigId),
                ("GenerationModelVersion", request.GenerationModelVersion), ("UpdatedAtUtc", clock.UtcNow));
            var affected = await command.ExecuteAsync(AiKnowledgeScope.IsTenant(tenant) ? AiKnowledgeSql.PolicyTenant : AiKnowledgeSql.PolicyHost, parameters, ct).ConfigureAwait(false);
            return affected == 1 ? await GetAsync(id, owner, ct).ConfigureAwait(false) : Conflict();
        }, token);

    private Task<AiKnowledgeBaseRecord?> FindAsync(Guid id, Guid owner, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<AiKnowledgeBaseRecord>(AiKnowledgeScope.IsTenant(tenant) ? AiKnowledgeSql.FindTenant : AiKnowledgeSql.FindHost,
            AiSqlParameters.Create(("Id", id), ("OwnerUserId", owner)), token);

    private async Task<bool> IsAvailableModelAsync(Guid? id, int? version, CancellationToken token)
    {
        if (id is null) return true;
        // 配置版本冻结包含实际目的地；R05/R06 在处理前必须再次校验，不能凭 Provider 名推断内网。
        var model = await query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(AiKnowledgeScope.IsTenant(tenant)
                ? AiModelConfigSql.FindAvailableForTenantChat : AiModelConfigSql.FindAvailableForHostChat,
            AiSqlParameters.Create(("ModelConfigId", id)), token).ConfigureAwait(false);
        return model is { IsEnabled: true } && model.Version == version;
    }

    private static string? ValidateMetadata(string? name, string? description) =>
        string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200 || description?.Length > 2000
            ? "Name is required and limited to 200 characters; description is limited to 2000 characters." : null;

    private static AiKnowledgeBaseResponse Map(AiKnowledgeBaseRecord row) => new(row.Id, row.Name, row.Description, row.IsEnabled,
        row.DataClassification, row.EmbeddingModelConfigId, row.EmbeddingModelVersion, row.GenerationModelConfigId,
        row.GenerationModelVersion, row.CreatedAtUtc, row.UpdatedAtUtc, row.Version);
    private static Result<T> Failure<T>(string code, string message, ErrorType type) => Result<T>.Failure(new Error(code, message, type));
    private static Result<AiKnowledgeBaseResponse> Invalid(string message) => Failure<AiKnowledgeBaseResponse>(AiKnowledgeErrorCodes.InputInvalid, message, ErrorType.Validation);
    private static Result<AiKnowledgeBaseResponse> NotFound() => Failure<AiKnowledgeBaseResponse>(AiKnowledgeErrorCodes.NotFound, "The knowledge base was not found.", ErrorType.NotFound);
    private static Result<AiKnowledgeBaseResponse> Conflict() => Failure<AiKnowledgeBaseResponse>(AiKnowledgeErrorCodes.VersionConflict, "The knowledge base was modified by another request.", ErrorType.Conflict);
}
