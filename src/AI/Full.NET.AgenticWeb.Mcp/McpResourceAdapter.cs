using Full.NET.Agents.Mcp;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Full.NET.AgenticWeb.Mcp;

/// <summary>仅暴露受限会话摘要资源 URI；禁止任意文件或 SQL 映射。</summary>
internal sealed class McpResourceAdapter
{
    internal async ValueTask<ListResourcesResult> ListResourcesAsync(
        RequestContext<ListResourcesRequestParams> context,
        CancellationToken cancellationToken)
    {
        _ = context;
        await Task.CompletedTask.ConfigureAwait(false);
        return new ListResourcesResult
        {
            Resources =
            [
                new Resource
                {
                    Uri = McpExposurePolicy.SessionSummaryUriTemplate,
                    Name = "chat-session-summary",
                    Description = "Owned chat session summary for the current principal.",
                    MimeType = "application/json",
                },
            ],
        };
    }

    internal async ValueTask<ReadResourceResult> ReadResourceAsync(
        RequestContext<ReadResourceRequestParams> context,
        CancellationToken cancellationToken)
    {
        var uri = context.Params?.Uri ?? string.Empty;
        var reader = ResolveReader(context.Services);
        var payload = await reader.TryReadSummaryJsonAsync(uri, cancellationToken).ConfigureAwait(false);
        if (payload is null)
        {
            return new ReadResourceResult
            {
                Contents =
                [
                    new TextResourceContents
                    {
                        Uri = uri,
                        MimeType = "text/plain",
                        Text = "Resource is not authorized or does not exist.",
                    },
                ],
            };
        }

        return new ReadResourceResult
        {
            Contents =
            [
                new TextResourceContents
                {
                    Uri = uri,
                    MimeType = "application/json",
                    Text = payload,
                },
            ],
        };
    }

    private static IMcpSessionSummaryReader ResolveReader(IServiceProvider? services)
    {
        if (services is null)
        {
            throw new InvalidOperationException("MCP request services are unavailable.");
        }

        return services.GetRequiredService<IMcpSessionSummaryReader>();
    }
}
