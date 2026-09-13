using System.Text.Json;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Modules.Ai.Serialization;

namespace Full.NET.Modules.Ai.Features.ManageAgentTools.Handlers;

/// <summary>显式只读健康 Handler，不执行用户提供的表达式。</summary>
internal sealed class PingToolHandler : IAgentToolHandler
{
    public bool ValidateArguments(JsonElement arguments) => arguments.ValueKind == JsonValueKind.Object && !arguments.EnumerateObject().Any();
    public ValueTask<JsonElement> ExecuteAsync(ToolInvocation invocation, ToolActor actor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ValidateArguments(invocation.Arguments)) throw new InvalidOperationException("Invalid tool arguments.");
        return ValueTask.FromResult(JsonSerializer.SerializeToElement(new ToolPingResult(true), AiToolJsonSerializerContext.Default.ToolPingResult));
    }
}
