using Full.NET.Abstractions.Results;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Agents.Tools;

namespace Full.NET.Modules.Ai.Features.ManageAgentTools;

/// <summary>静态 Agent Tool 目录查询。</summary>
internal sealed class AiAgentToolCatalogService(IAgentToolRegistrySource registrySource, IToolAuthorizationPort authorization)
{
    /// <summary>只列出当前主体有权执行且存在 Handler 的只读工具。</summary>
    public async Task<Result<IReadOnlyList<AiAgentToolCatalogItem>>> ListAsync(CancellationToken cancellationToken)
    {
        var visible = new List<AiAgentToolCatalogItem>();
        foreach (var item in AiAgentToolCatalog.List())
            if (await IsVisibleAsync(item, cancellationToken).ConfigureAwait(false)) visible.Add(item);
        return Result<IReadOnlyList<AiAgentToolCatalogItem>>.Success(visible);
    }

    /// <summary>按工具名读取目录项。</summary>
    /// <param name="toolName">工具名。</param>
    public async Task<Result<AiAgentToolCatalogItem>> GetByNameAsync(string toolName, CancellationToken cancellationToken)
    {
        var item = AiAgentToolCatalog.Find(toolName);
        return item is null || !await IsVisibleAsync(item, cancellationToken).ConfigureAwait(false)
            ? Result<AiAgentToolCatalogItem>.Failure(new Error(
                AiErrorCodes.AgentToolNotFound,
                "The AI agent tool was not found.",
                ErrorType.NotFound))
            : Result<AiAgentToolCatalogItem>.Success(item);
    }

    private async Task<bool> IsVisibleAsync(AiAgentToolCatalogItem item, CancellationToken cancellationToken)
    {
        var registry = await registrySource.GetRegistryAsync(cancellationToken).ConfigureAwait(false);
        var definition = registry.Find(item.ToolName);
        return item.IsEnabled && item.McpExposureKey != "planned"
            && definition is { IsEnabled: true, Handler: not null, SideEffectKey: "none" or "read" }
            && definition.PermissionCode == item.PermissionCode
            && await authorization.AuthorizeAsync(definition.PermissionCode, cancellationToken).ConfigureAwait(false) is not null;
    }
}
