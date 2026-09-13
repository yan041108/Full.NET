using System.Text;
using System.Text.Json;
using Full.NET.Agents.Mcp;
using Full.NET.AI.Abstractions.Tools;
using ModelContextProtocol.Protocol;

namespace Full.NET.AgenticWeb.Mcp.Client;

/// <summary>将已批准远端工具代理为本地 Handler；执行前校验 Schema 漂移。</summary>
internal sealed class McpRemoteToolHandler(
    McpRemoteApprovedTool tool,
    McpClientConnectionManager connections) : IAgentToolHandler
{
    public bool ValidateArguments(JsonElement arguments) =>
        arguments.ValueKind == JsonValueKind.Object
        && Encoding.UTF8.GetByteCount(arguments.GetRawText()) <= 16384;

    public async ValueTask<JsonElement> ExecuteAsync(
        ToolInvocation invocation,
        ToolActor actor,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ValidateArguments(invocation.Arguments))
        {
            throw new InvalidOperationException("Invalid tool arguments.");
        }

        var client = await connections.GetClientAsync(
            tool.ConnectionId,
            tool.EndpointUrl,
            tool.ServiceAccessToken,
            cancellationToken).ConfigureAwait(false);
        var liveTools = await client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var live = liveTools.FirstOrDefault(item => string.Equals(item.Name, tool.RemoteToolName, StringComparison.Ordinal));
        string? liveHash = null;
        if (live is not null && live.JsonSchema.ValueKind != JsonValueKind.Undefined)
        {
            liveHash = McpRemoteCapabilityPolicy.ComputeSchemaHash(live.JsonSchema.GetRawText());
        }
        if (!McpRemoteCapabilityPolicy.CanExecute(tool, liveHash, out var rejection))
        {
            throw new InvalidOperationException(rejection ?? "ai.mcp.remote_not_approved");
        }

        var arguments = JsonSerializer.Deserialize<Dictionary<string, object?>>(invocation.Arguments.GetRawText())
            ?? new Dictionary<string, object?>();
        var result = await client.CallToolAsync(tool.RemoteToolName, arguments, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (result.IsError ?? false)
        {
            throw new InvalidOperationException(result.Content?.FirstOrDefault()?.ToString() ?? "ai.mcp.remote_call_failed");
        }

        var text = result.Content?.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "null";
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}
