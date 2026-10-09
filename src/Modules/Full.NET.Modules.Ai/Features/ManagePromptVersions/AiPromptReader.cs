using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Features.ManagePromptVersions;

/// <summary>只读取 AI 自有目录和当前模型准入；调用者独立承担当前会话授权。</summary>
internal sealed class AiPromptReader(IQueryExecutor queries)
{
    internal static Dictionary<string, object?> Parameters(AiChatScope scope) => new(AiSqlParameters.Create(
        ("ScopeKey", scope.TenantId?.ToString("N") ?? "host")));
    internal Task<AiPromptRecord?> HeadAsync(AiChatScope scope, Guid id, CancellationToken token)
    {
        var p = Parameters(scope); p["PromptId"] = id;
        return queries.QuerySingleOrDefaultAsync<AiPromptRecord>(AiPromptSql.HeadFor(scope.TenantId.HasValue), p, token);
    }
    internal Task<AiPromptVersionRecord?> VersionAsync(AiChatScope scope, Guid id, CancellationToken token)
    {
        var p = Parameters(scope); p["VersionId"] = id;
        return queries.QuerySingleOrDefaultAsync<AiPromptVersionRecord>(AiPromptSql.VersionFor(scope.TenantId.HasValue), p, token);
    }
    internal Task<AiModelConfigRecord?> ModelAsync(AiChatScope scope, Guid id, CancellationToken token) =>
        queries.QuerySingleOrDefaultAsync<AiModelConfigRecord>(scope.TenantId.HasValue ? AiModelConfigSql.FindAvailableForTenantChat
            : AiModelConfigSql.FindAvailableForHostChat, AiSqlParameters.Create(("ModelConfigId", id)), token);
    internal static bool ValidHead(AiChatScope scope, AiPromptRecord? row) => row is not null && row.Id != Guid.Empty
        && row.TenantId == scope.TenantId && row.ScopeKey == (scope.TenantId?.ToString("N") ?? "host")
        && row.Revision >= row.LastVersion && row.LastVersion >= 1 && row.Epoch >= 0;
}
