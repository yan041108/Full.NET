using Full.NET.Modules.Ai.Contracts;

namespace Full.NET.Modules.Ai.Domain;

/// <summary>AI 模型配置字段校验与提供程序默认端点。</summary>
internal static class AiModelConfigFieldValidator
{
    /// <summary>名称允许的最大字符数。</summary>
    internal const int MaxNameLength = 128;

    /// <summary>端点基址允许的最大字符数。</summary>
    internal const int MaxEndpointLength = 512;

    /// <summary>模型标识允许的最大字符数。</summary>
    internal const int MaxModelIdLength = 128;

    /// <summary>组织标识允许的最大字符数。</summary>
    internal const int MaxOrganizationIdLength = 128;

    /// <summary>OpenAI 兼容 API 默认端点。</summary>
    internal const string DefaultOpenAiCompatibleEndpoint = "https://api.openai.com/v1";

    /// <summary>Azure OpenAI 默认端点占位；实际资源地址由管理员配置。</summary>
    internal const string DefaultAzureOpenAiEndpoint = "https://example.openai.azure.com";

    /// <summary>Ollama 默认端点。</summary>
    internal const string DefaultOllamaEndpoint = "http://127.0.0.1:11434";

    /// <summary>校验提供程序键是否在受支持白名单内。</summary>
    /// <param name="providerKey">提供程序键。</param>
    /// <returns>是否受支持。</returns>
    public static bool IsSupportedProvider(string? providerKey) =>
        string.Equals(providerKey, AiProviderKeys.OpenAiCompatible, StringComparison.Ordinal)
        || string.Equals(providerKey, AiProviderKeys.Ollama, StringComparison.Ordinal)
        || string.Equals(providerKey, AiProviderKeys.AzureOpenAi, StringComparison.Ordinal);

    /// <summary>解析提供程序默认端点基址。</summary>
    /// <param name="providerKey">提供程序键。</param>
    /// <returns>默认端点。</returns>
    public static string ResolveDefaultEndpoint(string providerKey) =>
        string.Equals(providerKey, AiProviderKeys.Ollama, StringComparison.Ordinal)
            ? DefaultOllamaEndpoint
            : string.Equals(providerKey, AiProviderKeys.AzureOpenAi, StringComparison.Ordinal)
                ? DefaultAzureOpenAiEndpoint
                : DefaultOpenAiCompatibleEndpoint;

    /// <summary>指定提供程序是否要求 API 密钥。</summary>
    /// <param name="providerKey">提供程序键。</param>
    /// <returns>是否要求密钥。</returns>
    public static bool RequiresApiKey(string providerKey) =>
        string.Equals(providerKey, AiProviderKeys.OpenAiCompatible, StringComparison.Ordinal)
        || string.Equals(providerKey, AiProviderKeys.AzureOpenAi, StringComparison.Ordinal);

    /// <summary>校验模型配置元数据。</summary>
    /// <param name="name">显示名称。</param>
    /// <param name="providerKey">提供程序键。</param>
    /// <param name="endpointBaseUrl">端点基址。</param>
    /// <param name="modelId">模型标识。</param>
    /// <param name="organizationId">组织标识。</param>
    /// <returns>校验失败时的错误消息；成功时为 <see langword="null"/>。</returns>
    public static string? ValidateMetadata(
        string name,
        string providerKey,
        string endpointBaseUrl,
        string modelId,
        string? organizationId)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaxNameLength)
        {
            return "Name is required and must not exceed 128 characters.";
        }

        if (!IsSupportedProvider(providerKey))
        {
            return "Provider key must be openai_compatible, ollama, or azure_openai.";
        }

        if (!IsSafeEndpoint(endpointBaseUrl))
        {
            return "Endpoint base URL must be an absolute http or https URL without credentials.";
        }

        if (string.IsNullOrWhiteSpace(modelId) || modelId.Trim().Length > MaxModelIdLength)
        {
            return "Model id is required and must not exceed 128 characters.";
        }

        if (organizationId is not null
            && (organizationId.Trim().Length == 0 || organizationId.Trim().Length > MaxOrganizationIdLength))
        {
            return "Organization id must not exceed 128 characters.";
        }

        return null;
    }

    /// <summary>校验租户配额上限。</summary>
    /// <param name="monthlyTokenLimit">每月 Token 上限。</param>
    /// <param name="monthlyRequestLimit">每月请求次数上限。</param>
    /// <returns>校验失败时的错误消息；成功时为 <see langword="null"/>。</returns>
    public static string? ValidateQuotaLimits(long? monthlyTokenLimit, long? monthlyRequestLimit)
    {
        if (monthlyTokenLimit is < 0)
        {
            return "Monthly token limit must be zero or positive when specified.";
        }

        if (monthlyRequestLimit is < 0)
        {
            return "Monthly request limit must be zero or positive when specified.";
        }

        return null;
    }

    private static bool IsSafeEndpoint(string endpointBaseUrl)
    {
        if (!Uri.TryCreate(endpointBaseUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        return uri.Host.Length > 0;
    }
}
