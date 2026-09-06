using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Features.ManageModelConfigs;

/// <summary>AI 模型配置响应映射。</summary>
internal static class AiModelConfigMapper
{
    /// <summary>映射列表项（脱敏）。</summary>
    /// <param name="row">持久化行。</param>
    /// <returns>列表响应。</returns>
    public static AiModelConfigListItem MapListItem(AiModelConfigRecord row) =>
        new(
            row.Id,
            row.TenantId,
            row.Name,
            row.ProviderKey,
            AiModelConfigMasking.MaskEndpoint(row.EndpointBaseUrl),
            row.ModelId,
            HasApiKey(row),
            row.IsDefault,
            row.IsEnabled,
            row.LastTestedAtUtc,
            row.LastTestStatusKey,
            row.LastTestMessage,
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.Version);

    /// <summary>映射详情（不回显 API 密钥）。</summary>
    /// <param name="row">持久化行。</param>
    /// <returns>详情响应。</returns>
    public static AiModelConfigResponse MapDetail(AiModelConfigRecord row) =>
        new(
            row.Id,
            row.TenantId,
            row.Name,
            row.ProviderKey,
            row.EndpointBaseUrl,
            row.ModelId,
            row.OrganizationId,
            HasApiKey(row),
            row.IsDefault,
            row.IsEnabled,
            row.LastTestedAtUtc,
            row.LastTestStatusKey,
            row.LastTestMessage,
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.Version);

    private static bool HasApiKey(AiModelConfigRecord row) =>
        !string.IsNullOrWhiteSpace(row.ApiKeyProtected);
}
