using System.Security.Cryptography;
using System.Text;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Features.ManagePromptVersions;

/// <summary>目录与版本属于 AI 本地事务；身份授权在事务外，锁内复核正文、模型修订及发布指针。</summary>
internal sealed class AiPromptManagementService(AiPromptReader reader, IQueryExecutor queries, ICommandExecutor commands,
    ICommandTransaction transaction, IDataTransactionState transactions, ICurrentTenant tenant,
    ICurrentSessionAuthorization authorization, IOptions<DatabaseOptions> database, IIdGenerator ids, IClock clock)
{
    internal async Task<Result<PagedResult<AiPromptResponse>>> ListAsync(Guid actor, int page, int pageSize, CancellationToken token)
    {
        Outside(); var scope = AiChatScope.Resolve(tenant);
        if (!await AuthorizedAsync(scope, actor, AiPromptPermissions.Read, token).ConfigureAwait(false)) return Failure<PagedResult<AiPromptResponse>>("authorization.permission_denied", ErrorType.Forbidden);
        var p = PageParameters(scope, page, pageSize, out page, out pageSize);
        var total = await queries.QuerySingleOrDefaultAsync<long>(AiPromptSql.ListFor(database.Value.Provider, scope.TenantId.HasValue, false, true), p, token).ConfigureAwait(false);
        var rows = await queries.QueryAsync<AiPromptRecord>(AiPromptSql.ListFor(database.Value.Provider, scope.TenantId.HasValue, false, false), p, token).ConfigureAwait(false);
        if (rows.Any(row => !AiPromptReader.ValidHead(scope, row))) return Failure<PagedResult<AiPromptResponse>>("ai.prompts.unavailable", ErrorType.NotFound);
        if (!await AuthorizedAsync(scope, actor, AiPromptPermissions.Read, token).ConfigureAwait(false)) return Failure<PagedResult<AiPromptResponse>>("authorization.permission_denied", ErrorType.Forbidden);
        return Result<PagedResult<AiPromptResponse>>.Success(new(rows.Select(Map).ToArray(), page, pageSize, total));
    }

    internal async Task<Result<PagedResult<AiPromptVersionResponse>>> VersionsAsync(Guid actor, Guid id, int page, int pageSize, CancellationToken token)
    {
        Outside(); var scope = AiChatScope.Resolve(tenant);
        if (!await AuthorizedAsync(scope, actor, AiPromptPermissions.Read, token).ConfigureAwait(false)) return Failure<PagedResult<AiPromptVersionResponse>>("authorization.permission_denied", ErrorType.Forbidden);
        if (!AiPromptReader.ValidHead(scope, await reader.HeadAsync(scope, id, token).ConfigureAwait(false))) return Failure<PagedResult<AiPromptVersionResponse>>("ai.prompts.not_found", ErrorType.NotFound);
        var p = PageParameters(scope, page, pageSize, out page, out pageSize); p["PromptId"] = id;
        var total = await queries.QuerySingleOrDefaultAsync<long>(AiPromptSql.ListFor(database.Value.Provider, scope.TenantId.HasValue, true, true), p, token).ConfigureAwait(false);
        var rows = await queries.QueryAsync<AiPromptVersionRecord>(AiPromptSql.ListFor(database.Value.Provider, scope.TenantId.HasValue, true, false), p, token).ConfigureAwait(false);
        var mapped = new List<AiPromptVersionResponse>();
        foreach (var row in rows)
        {
            var body = AiPromptVersionIntegrity.Decode(row);
            if (body is null || row.PromptId != id || row.TenantId != scope.TenantId || row.ScopeKey != ScopeKey(scope)) return Failure<PagedResult<AiPromptVersionResponse>>("ai.prompts.unavailable", ErrorType.NotFound);
            mapped.Add(new(row.Id, id, row.Version, body, row.ModelConfigVersion, row.ProviderKey, row.ModelId, row.BodyHash, row.WasPublished, row.CreatedAtUtc));
        }
        if (!await AuthorizedAsync(scope, actor, AiPromptPermissions.Read, token).ConfigureAwait(false)) return Failure<PagedResult<AiPromptVersionResponse>>("authorization.permission_denied", ErrorType.Forbidden);
        return Result<PagedResult<AiPromptVersionResponse>>.Success(new(mapped, page, pageSize, total));
    }

    internal async Task<Result<AiPromptResponse>> CreateAsync(Guid actor, CreateAiPromptRequest request, CancellationToken token)
    {
        Outside(); var scope = AiChatScope.Resolve(tenant);
        if (!await AuthorizedAsync(scope, actor, AiPromptPermissions.Create, token).ConfigureAwait(false)) return Forbidden();
        if (request.ClientRequestId == Guid.Empty || request.Name is not { Length: > 0 and <= 128 } name
            || string.IsNullOrWhiteSpace(name) || name != name.Trim() || !AiPromptPolicy.ValidUnicode(name) || !AiPromptPolicy.IsValid(request.Body)) return Invalid();
        var bodyJson = AiPromptVersionIntegrity.Serialize(request.Body);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(FormattableString.Invariant($"{name.Length}:{name}|{bodyJson}"))));
        var p = AiPromptReader.Parameters(scope); p["ActorUserId"] = actor; p["ClientRequestId"] = request.ClientRequestId;
        var existing = await queries.QuerySingleOrDefaultAsync<AiPromptRecord>(AiPromptSql.ClientFor(scope.TenantId.HasValue), p, token).ConfigureAwait(false);
        if (existing is not null) return Same(scope) && AiPromptReader.ValidHead(scope, existing) && existing.CreateHash == hash ? Success(existing) : Conflict();
        var model = await reader.ModelAsync(scope, request.Body.ModelConfigId, token).ConfigureAwait(false);
        if (!ValidModel(model) || !Same(scope)) return Unavailable();
        SetBody(p, request.Body, model!); p["PromptId"] = ids.NewId(); p["VersionId"] = ids.NewId();
        p["CreateHash"] = hash; p["Name"] = name; p["Number"] = 1; p["Now"] = clock.UtcNow;
        try
        {
            return await transaction.ExecuteResultAsync(async ct =>
            {
                if (!Same(scope) || !await ModelMatchesAsync(scope, request.Body, model!, ct).ConfigureAwait(false)) return Unavailable();
                ct.ThrowIfCancellationRequested();
                if (await commands.ExecuteAsync(AiPromptSql.InsertHeadFor(scope.TenantId.HasValue), p, ct).ConfigureAwait(false) != 1
                    || await commands.ExecuteAsync(AiPromptSql.InsertVersionFor(scope.TenantId.HasValue), p, ct).ConfigureAwait(false) != 1) return Conflict();
                return await ReadWrittenAsync(scope, (Guid)p["PromptId"]!, ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex.Message.Contains("UX_fn_ai_prompt_ScopeKey_OwnerUserId_ClientRequestId", StringComparison.OrdinalIgnoreCase))
        {
            // 唯一竞争必须先回滚；只恢复同一主体、同一规范载荷，不能覆盖另一个目录。
            existing = await queries.QuerySingleOrDefaultAsync<AiPromptRecord>(AiPromptSql.ClientFor(scope.TenantId.HasValue), p, token).ConfigureAwait(false);
            return Same(scope) && AiPromptReader.ValidHead(scope, existing) && existing!.CreateHash == hash ? Success(existing) : Conflict();
        }
    }

    internal async Task<Result<AiPromptResponse>> AppendAsync(Guid actor, Guid id, AppendAiPromptVersionRequest request, CancellationToken token)
    {
        Outside(); var scope = AiChatScope.Resolve(tenant);
        if (!await AuthorizedAsync(scope, actor, AiPromptPermissions.Append, token).ConfigureAwait(false)) return Forbidden();
        if (request.Revision is < 1 or int.MaxValue || !AiPromptPolicy.IsValid(request.Body)) return Invalid();
        var model = await reader.ModelAsync(scope, request.Body.ModelConfigId, token).ConfigureAwait(false);
        if (!ValidModel(model) || !Same(scope)) return Unavailable();
        var p = AiPromptReader.Parameters(scope); p["PromptId"] = id; p["ActorUserId"] = actor;
        p["VersionId"] = ids.NewId(); p["Revision"] = request.Revision; p["Now"] = clock.UtcNow; SetBody(p, request.Body, model!);
        return await transaction.ExecuteResultAsync(async ct =>
        {
            if (!await LockAsync(scope, p, ct).ConfigureAwait(false)) return NotFound();
            var head = await reader.HeadAsync(scope, id, ct).ConfigureAwait(false);
            if (!AiPromptReader.ValidHead(scope, head) || !Same(scope)) return NotFound();
            if (head!.Revision != request.Revision || head.LastVersion == int.MaxValue) return Conflict();
            if (!await ModelMatchesAsync(scope, request.Body, model!, ct).ConfigureAwait(false)) return Unavailable();
            p["PreviousNumber"] = head.LastVersion; p["Number"] = head.LastVersion + 1; ct.ThrowIfCancellationRequested();
            if (await commands.ExecuteAsync(AiPromptSql.InsertVersionFor(scope.TenantId.HasValue), p, ct).ConfigureAwait(false) != 1
                || await commands.ExecuteAsync(AiPromptSql.AppendFor(scope.TenantId.HasValue), p, ct).ConfigureAwait(false) != 1) return Conflict();
            return await ReadWrittenAsync(scope, id, ct).ConfigureAwait(false);
        }, token).ConfigureAwait(false);
    }

    internal async Task<Result<AiPromptResponse>> SelectAsync(Guid actor, Guid id, SelectAiPromptVersionRequest request, bool rollback, CancellationToken token)
    {
        Outside(); var scope = AiChatScope.Resolve(tenant);
        if (!await AuthorizedAsync(scope, actor, rollback ? AiPromptPermissions.Rollback : AiPromptPermissions.Publish, token).ConfigureAwait(false)) return Forbidden();
        if (request.Revision is < 1 or int.MaxValue || request.VersionId == Guid.Empty) return Invalid();
        var p = AiPromptReader.Parameters(scope); p["PromptId"] = id; p["VersionId"] = request.VersionId; p["Revision"] = request.Revision;
        return await transaction.ExecuteResultAsync(async ct =>
        {
            if (!await LockAsync(scope, p, ct).ConfigureAwait(false)) return NotFound();
            var head = await reader.HeadAsync(scope, id, ct).ConfigureAwait(false);
            var version = await reader.VersionAsync(scope, request.VersionId, ct).ConfigureAwait(false);
            var body = version is null ? null : AiPromptVersionIntegrity.Decode(version);
            if (!AiPromptReader.ValidHead(scope, head) || version?.PromptId != id || body is null || !Same(scope)) return NotFound();
            if (rollback && !version!.WasPublished) return Invalid();
            var model = await reader.ModelAsync(scope, body.ModelConfigId, ct).ConfigureAwait(false);
            if (!ValidModel(model) || model!.Version != version!.ModelConfigVersion || model.ProviderKey != version.ProviderKey || model.ModelId != version.ModelId) return Unavailable();
            if (head!.Revision == request.Revision + 1 && head.ActiveVersionId == request.VersionId && version.WasPublished) return Success(head);
            if (head.Revision != request.Revision) return Conflict();
            // 曾发布版本只能由回滚权限选择；发布只接受草稿，精确重试已在前面处理。
            if (!rollback && version.WasPublished) return Invalid();
            ct.ThrowIfCancellationRequested();
            if (!version.WasPublished && await commands.ExecuteAsync(AiPromptSql.MarkFor(scope.TenantId.HasValue), p, ct).ConfigureAwait(false) != 1) return Conflict();
            if (await commands.ExecuteAsync(AiPromptSql.PublishFor(scope.TenantId.HasValue), p, ct).ConfigureAwait(false) != 1) return Conflict();
            return await ReadWrittenAsync(scope, id, ct).ConfigureAwait(false);
        }, token).ConfigureAwait(false);
    }

    internal async Task<Result<AiPromptResponse>> DeactivateAsync(Guid actor, Guid id, DeactivateAiPromptRequest request, CancellationToken token)
    {
        Outside(); var scope = AiChatScope.Resolve(tenant);
        if (!await AuthorizedAsync(scope, actor, AiPromptPermissions.Deactivate, token).ConfigureAwait(false)) return Forbidden();
        if (request.Revision is < 1 or int.MaxValue) return Invalid();
        var p = AiPromptReader.Parameters(scope); p["PromptId"] = id; p["Revision"] = request.Revision;
        return await transaction.ExecuteResultAsync(async ct =>
        {
            if (!await LockAsync(scope, p, ct).ConfigureAwait(false)) return NotFound();
            var head = await reader.HeadAsync(scope, id, ct).ConfigureAwait(false);
            if (!AiPromptReader.ValidHead(scope, head) || !Same(scope)) return NotFound();
            if (head!.Revision == request.Revision + 1 && head.ActiveVersionId is null) return Success(head);
            if (head.Revision != request.Revision || head.Epoch == int.MaxValue) return Conflict();
            ct.ThrowIfCancellationRequested();
            if (await commands.ExecuteAsync(AiPromptSql.DeactivateFor(scope.TenantId.HasValue), p, ct).ConfigureAwait(false) != 1) return Conflict();
            return await ReadWrittenAsync(scope, id, ct).ConfigureAwait(false);
        }, token).ConfigureAwait(false);
    }

    private async Task<bool> AuthorizedAsync(AiChatScope scope, Guid actor, string permission, CancellationToken token)
    {
        foreach (var code in new[] { AiAgentRunPermissions.Read, AiPromptPermissions.Read, permission }.Distinct(StringComparer.Ordinal))
        {
            var current = await authorization.AuthorizeAsync(code, token).ConfigureAwait(false);
            if (current?.UserId != actor || current.TenantId != scope.TenantId || !Same(scope)) return false;
        }
        return true;
    }
    private bool Same(AiChatScope scope) => scope.TenantId == tenant.Id && (scope.TenantId.HasValue ? tenant.IsAvailable && !tenant.IsHost : tenant.IsHost);
    private void Outside() { if (transactions.HasTransaction) throw new InvalidOperationException("Prompt 授权不能加入已有事务。"); }
    private async Task<bool> ModelMatchesAsync(AiChatScope scope, AiPromptBody body, AiModelConfigRecord original, CancellationToken token)
    {
        var current = await reader.ModelAsync(scope, body.ModelConfigId, token).ConfigureAwait(false);
        return Same(scope) && ValidModel(current) && current!.Id == body.ModelConfigId && original.Id == body.ModelConfigId
            && current.Version == original.Version && current.ProviderKey == original.ProviderKey && current.ModelId == original.ModelId;
    }
    private static bool ValidModel(AiModelConfigRecord? row) => row is { IsEnabled: true, Version: > 0 } && row.ProviderKey.Length is > 0 and <= 64 && row.ModelId.Length is > 0 and <= 256 && AiPromptPolicy.ValidUnicode(row.ProviderKey) && AiPromptPolicy.ValidUnicode(row.ModelId);
    private static string ScopeKey(AiChatScope scope) => scope.TenantId?.ToString("N") ?? "host";
    private static Dictionary<string, object?> PageParameters(AiChatScope scope, int inputPage, int inputSize, out int page, out int pageSize)
    {
        page = Math.Max(1, inputPage); pageSize = Math.Clamp(inputSize, 1, 100);
        var p = AiPromptReader.Parameters(scope); p["Offset"] = (long)(page - 1) * pageSize; p["PageSize"] = pageSize; return p;
    }
    private static void SetBody(Dictionary<string, object?> p, AiPromptBody body, AiModelConfigRecord model)
    {
        var json = AiPromptVersionIntegrity.Serialize(body);
        p["BodyJson"] = json; p["ModelConfigVersion"] = model.Version; p["ProviderKey"] = model.ProviderKey; p["ModelId"] = model.ModelId;
        p["BodyHash"] = AiPromptVersionIntegrity.Hash(json, model.Version, model.ProviderKey, model.ModelId);
    }
    private async Task<bool> LockAsync(AiChatScope scope, Dictionary<string, object?> p, CancellationToken token) =>
        await queries.QuerySingleOrDefaultAsync<Guid>(AiPromptSql.LockFor(database.Value.Provider, scope.TenantId.HasValue), p, token).ConfigureAwait(false) == (Guid)p["PromptId"]! && Same(scope);
    private async Task<Result<AiPromptResponse>> ReadWrittenAsync(AiChatScope scope, Guid id, CancellationToken token)
    {
        var row = await reader.HeadAsync(scope, id, token).ConfigureAwait(false); token.ThrowIfCancellationRequested();
        return Same(scope) && AiPromptReader.ValidHead(scope, row) ? Success(row!) : NotFound();
    }
    private static AiPromptResponse Map(AiPromptRecord row) => new(row.Id, row.Name, row.Revision, row.LastVersion, row.ActiveVersionId, row.Epoch);
    private static Result<AiPromptResponse> Success(AiPromptRecord row) => Result<AiPromptResponse>.Success(Map(row));
    private static Result<T> Failure<T>(string code, ErrorType type) => Result<T>.Failure(new Error(code, "The Prompt operation is unavailable.", type));
    private static Result<AiPromptResponse> Forbidden() => Failure<AiPromptResponse>("authorization.permission_denied", ErrorType.Forbidden);
    private static Result<AiPromptResponse> Invalid() => Failure<AiPromptResponse>("ai.prompts.invalid", ErrorType.Validation);
    private static Result<AiPromptResponse> Conflict() => Failure<AiPromptResponse>("ai.prompts.conflict", ErrorType.Conflict);
    private static Result<AiPromptResponse> NotFound() => Failure<AiPromptResponse>("ai.prompts.not_found", ErrorType.NotFound);
    private static Result<AiPromptResponse> Unavailable() => Failure<AiPromptResponse>("ai.prompts.unavailable", ErrorType.NotFound);
}
