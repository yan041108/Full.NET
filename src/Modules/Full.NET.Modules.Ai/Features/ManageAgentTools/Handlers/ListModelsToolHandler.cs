using System.Text.Json;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Serialization;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Features.ManageAgentTools.Handlers;

/// <summary>只读取当前作用域已启用的模型最小目录。</summary>
internal sealed class ListModelsToolHandler(IQueryExecutor queries, ICurrentTenant tenant, IOptions<DatabaseOptions> database) : IAgentToolHandler
{
    public bool ValidateArguments(JsonElement arguments) => ToolArguments.Parse(arguments) is not null;
    public async ValueTask<JsonElement> ExecuteAsync(ToolInvocation invocation, ToolActor actor, CancellationToken cancellationToken)
    {
        var page = ToolArguments.Parse(invocation.Arguments) ?? throw new InvalidOperationException("Invalid tool arguments.");
        if (actor.TenantId != tenant.Id || (actor.TenantId is null && !tenant.IsHost)) throw new InvalidOperationException("Tool scope mismatch.");
        var rows = await queries.QueryAsync<ToolModelItem>(database.Value.Provider == DatabaseProvider.SqlServer
            ? AiToolExecutionSql.ListModelsSqlServer : AiToolExecutionSql.ListModelsMySql,
            AiSqlParameters.Create(("ScopeTenantId", actor.TenantId), ("Offset", (page.Page - 1) * page.PageSize), ("PageSize", page.PageSize)),
            cancellationToken).ConfigureAwait(false);
        return JsonSerializer.SerializeToElement(new ToolModelList(rows), AiToolJsonSerializerContext.Default.ToolModelList);
    }
}

/// <summary>固定模型列表结果；不暴露内部持久化对象。</summary>
internal sealed record ToolModelList([property: System.Text.Json.Serialization.JsonPropertyName("items")] IReadOnlyList<ToolModelItem> Items);
