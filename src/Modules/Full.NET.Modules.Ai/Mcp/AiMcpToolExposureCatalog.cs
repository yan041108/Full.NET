using Full.NET.Agents.Mcp;
using Full.NET.Agents.Tools;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;

namespace Full.NET.Modules.Ai.Mcp;

/// <summary>按 MCP 暴露策略与逐次授权过滤工具目录。</summary>
internal sealed class AiMcpToolExposureCatalog(
    IAgentToolRegistrySource registrySource,
    IToolAuthorizationPort authorization) : IMcpToolExposureCatalog
{
    public async ValueTask<IReadOnlyList<McpExposedToolDescriptor>> ListAuthorizedAsync(
        CancellationToken cancellationToken = default)
    {
        var registry = await registrySource.GetRegistryAsync(cancellationToken).ConfigureAwait(false);
        var visible = new List<McpExposedToolDescriptor>();
        foreach (var item in AiAgentToolCatalog.List())
        {
            if (!await IsExposableAsync(registry, item, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            visible.Add(new(
                item.ToolName,
                item.DisplayName,
                item.Description,
                item.InputSchemaJson,
                registry.Find(item.ToolName)?.Version ?? 1));
        }

        return visible;
    }

    public async ValueTask<McpExposedToolDescriptor?> FindAuthorizedAsync(
        string toolName,
        CancellationToken cancellationToken = default)
    {
        var item = AiAgentToolCatalog.Find(toolName);
        var registry = await registrySource.GetRegistryAsync(cancellationToken).ConfigureAwait(false);
        if (item is null || !await IsExposableAsync(registry, item, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new(
            item.ToolName,
            item.DisplayName,
            item.Description,
            item.InputSchemaJson,
            registry.Find(item.ToolName)?.Version ?? 1);
    }

    private async Task<bool> IsExposableAsync(
        AgentToolRegistry registry,
        AiAgentToolCatalogItem item,
        CancellationToken cancellationToken)
    {
        var definition = registry.Find(item.ToolName);
        return McpExposurePolicy.IsCatalogItemExposable(
                item.McpExposureKey,
                item.SideEffectKey,
                item.IsEnabled,
                definition is { IsEnabled: true, Handler: not null })
            && definition!.PermissionCode == item.PermissionCode
            && await authorization.AuthorizeAsync(definition.PermissionCode, cancellationToken).ConfigureAwait(false)
                is not null;
    }
}
