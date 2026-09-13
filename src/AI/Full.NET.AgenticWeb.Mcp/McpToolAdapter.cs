using System.Text.Json;
using Full.NET.Agents.Mcp;
using Full.NET.Agents.Tools;
using Full.NET.AI.Abstractions.Tools;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Full.NET.AgenticWeb.Mcp;

/// <summary>将受控 Agent 工具映射为 MCP list/call；每次调用独立授权与审计。</summary>
internal sealed class McpToolAdapter(IAgentToolExecutor executor)
{
    internal async ValueTask<ListToolsResult> ListToolsAsync(
        RequestContext<ListToolsRequestParams> context,
        CancellationToken cancellationToken)
    {
        var catalog = ResolveCatalog(context.Services);
        var tools = await catalog.ListAuthorizedAsync(cancellationToken).ConfigureAwait(false);
        var result = new ListToolsResult();
        foreach (var item in tools)
        {
            result.Tools.Add(new Tool
            {
                Name = item.ToolName,
                Title = item.DisplayName,
                Description = item.Description,
                InputSchema = JsonDocument.Parse(item.InputSchemaJson).RootElement,
            });
        }

        return result;
    }

    internal async ValueTask<CallToolResult> CallToolAsync(
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken)
    {
        var catalog = ResolveCatalog(context.Services);
        var toolName = context.Params?.Name ?? string.Empty;
        var descriptor = await catalog.FindAuthorizedAsync(toolName, cancellationToken).ConfigureAwait(false);
        if (descriptor is null)
        {
            return new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = "Tool is not authorized or does not exist." }],
            };
        }

        using var arguments = SerializeArguments(context.Params?.Arguments);
        var invocation = new ToolInvocation(
            Guid.CreateVersion7(),
            null,
            descriptor.ToolName,
            descriptor.ToolVersion,
            arguments.RootElement);
        var execution = await executor.ExecuteAsync(invocation, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(execution.StatusKey, "succeeded", StringComparison.Ordinal))
        {
            return new CallToolResult
            {
                IsError = true,
                Content =
                [
                    new TextContentBlock
                    {
                        Text = execution.ErrorCode ?? execution.StatusKey,
                    },
                ],
            };
        }

        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock
                {
                    Text = execution.Value?.GetRawText() ?? "null",
                },
            ],
        };
    }

    private static IMcpToolExposureCatalog ResolveCatalog(IServiceProvider? services) =>
        ResolveRequired<IMcpToolExposureCatalog>(services);

    private static JsonDocument SerializeArguments(IDictionary<string, JsonElement>? arguments) =>
        arguments is null || arguments.Count == 0
            ? JsonDocument.Parse("{}")
            : JsonDocument.Parse(JsonSerializer.Serialize(arguments));

    private static T ResolveRequired<T>(IServiceProvider? services) where T : notnull
    {
        if (services is null)
        {
            throw new InvalidOperationException("MCP request services are unavailable.");
        }

        return services.GetRequiredService<T>();
    }
}
