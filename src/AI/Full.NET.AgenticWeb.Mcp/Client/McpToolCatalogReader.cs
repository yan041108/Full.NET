using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Full.NET.AgenticWeb.Mcp.Client;

/// <summary>显式控制远端工具分页与目录预算；指定工具在找到后立即停止，执行前仍重新获取当前 Schema。</summary>
public static class McpToolCatalogReader
{
    /// <summary>读取有界目录，或定位一个工具；不以目录缓存替代执行前漂移检查。</summary>
    /// <param name="client">已经完成受控初始化的客户端。</param>
    /// <param name="toolName">精确工具名；为空时读取有界完整目录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>当前工具投影；超出资源预算时失败关闭。</returns>
    public static async Task<IReadOnlyList<Tool>> ReadAsync(
        McpClient client, string? toolName, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var result = new List<Tool>();
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        var count = 0;
        long bytes = 0;
        for (var page = 0; page < 16; page++)
        {
            var response = await client.ListToolsAsync(new ListToolsRequestParams { Cursor = cursor }, deadline.Token)
                .ConfigureAwait(false);
            count = checked(count + response.Tools.Count);
            if (count > 256) throw BudgetExceeded();
            foreach (var tool in response.Tools)
            {
                deadline.Token.ThrowIfCancellationRequested();
                var schemaBytes = tool.InputSchema.ValueKind == JsonValueKind.Undefined
                    ? 2 : Encoding.UTF8.GetByteCount(tool.InputSchema.GetRawText());
                bytes += schemaBytes + Encoding.UTF8.GetByteCount(tool.Name) + Encoding.UTF8.GetByteCount(tool.Description ?? string.Empty);
                if (schemaBytes > 64 * 1024 || bytes > 2 * 1024 * 1024) throw BudgetExceeded();
                if (toolName is null) result.Add(tool);
                else if (string.Equals(tool.Name, toolName, StringComparison.Ordinal)) return [tool];
            }
            cursor = response.NextCursor;
            if (cursor is null) return result;
            if (cursor.Length > 4096 || !cursors.Add(cursor)) throw BudgetExceeded();
        }
        throw BudgetExceeded();
    }

    private static InvalidOperationException BudgetExceeded() => new("ai.mcp.catalog_budget_exceeded");
}
