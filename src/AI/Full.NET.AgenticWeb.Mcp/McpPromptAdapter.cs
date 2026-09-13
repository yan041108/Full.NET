using System.Text.Json;
using Full.NET.Agents.Mcp;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Full.NET.AgenticWeb.Mcp;

/// <summary>静态提示模板；参数 Schema 受限，读取时再次校验权限。</summary>
internal sealed class McpPromptAdapter(IMcpSessionSummaryReader summaryReader)
{
    internal ValueTask<ListPromptsResult> ListPromptsAsync(
        RequestContext<ListPromptsRequestParams> context,
        CancellationToken cancellationToken)
    {
        _ = context;
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new ListPromptsResult
        {
            Prompts =
            [
                new Prompt
                {
                    Name = McpExposurePolicy.SessionSummaryPromptName,
                    Description = "Summarize an owned chat session using a static template.",
                    Arguments =
                    [
                        new PromptArgument
                        {
                            Name = "sessionId",
                            Description = "Owned chat session identifier.",
                            Required = true,
                        },
                    ],
                },
            ],
        });
    }

    internal async ValueTask<GetPromptResult> GetPromptAsync(
        RequestContext<GetPromptRequestParams> context,
        CancellationToken cancellationToken)
    {
        var promptName = context.Params?.Name ?? string.Empty;
        if (!string.Equals(promptName, McpExposurePolicy.SessionSummaryPromptName, StringComparison.Ordinal))
        {
            throw new McpException("Prompt is not available.");
        }

        if (!TryResolveSessionId(context.Params?.Arguments, out var sessionId))
        {
            throw new McpException("sessionId is required.");
        }

        var resourceUri = McpExposurePolicy.SessionSummaryUriTemplate.Replace(
            "{sessionId}",
            sessionId.ToString("D"),
            StringComparison.Ordinal);
        var summaryJson = await summaryReader.TryReadSummaryJsonAsync(resourceUri, cancellationToken)
            .ConfigureAwait(false);
        if (summaryJson is null)
        {
            throw new McpException("Session is not authorized or does not exist.");
        }

        return new GetPromptResult
        {
            Description = "Owned chat session summary.",
            Messages =
            [
                new PromptMessage
                {
                    Role = Role.User,
                    Content = new TextContentBlock
                    {
                        Text = $"Summarize this owned chat session using the provided JSON context:\n{summaryJson}",
                    },
                },
            ],
        };
    }

    private static bool TryResolveSessionId(IDictionary<string, JsonElement>? arguments, out Guid sessionId)
    {
        sessionId = default;
        if (arguments is null || !arguments.TryGetValue("sessionId", out var value))
        {
            return false;
        }

        return value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out sessionId);
    }
}
