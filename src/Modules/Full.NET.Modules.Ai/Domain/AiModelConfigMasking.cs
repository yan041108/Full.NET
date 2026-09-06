namespace Full.NET.Modules.Ai.Domain;

/// <summary>AI 模型配置列表脱敏辅助。</summary>
internal static class AiModelConfigMasking
{
    /// <summary>脱敏端点基址显示文本。</summary>
    /// <param name="endpointBaseUrl">端点基址。</param>
    /// <returns>脱敏后的 URL。</returns>
    public static string MaskEndpoint(string endpointBaseUrl)
    {
        if (!Uri.TryCreate(endpointBaseUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return "***";
        }

        var host = uri.Host;
        var maskedHost = host.Length <= 4
            ? "***"
            : host[..2] + "***" + host[^1];
        return $"{uri.Scheme}://{maskedHost}{uri.PathAndQuery}";
    }
}
