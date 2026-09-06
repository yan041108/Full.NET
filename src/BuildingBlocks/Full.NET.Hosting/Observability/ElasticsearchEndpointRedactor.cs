namespace Full.NET.Hosting.Observability;

/// <summary>对外暴露 Elasticsearch 节点 URI 的安全展示形式。</summary>
public static class ElasticsearchEndpointRedactor
{
    /// <summary>将节点 URI 归一化为不含凭据的 scheme://host:port 文本。</summary>
    /// <param name="nodeUri">原始节点 URI。</param>
    /// <returns>可安全展示的端点；非法 URI 返回 <see langword="null"/>。</returns>
    public static string? Redact(string nodeUri)
    {
        if (!Uri.TryCreate(nodeUri?.Trim(), UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        var builder = new UriBuilder(parsed.Scheme, parsed.Host, parsed.Port);
        return builder.Uri.GetLeftPart(UriPartial.Authority);
    }
}
