using Full.NET.Modules.Ocr.Contracts;
using Full.NET.Modules.Ocr.Persistence;

/// <summary>契约与持久化行之间的映射；不包含授权或副作用。</summary>
namespace Full.NET.Modules.Ocr.Features.ManageProviderConfigs;

internal static class OcrProviderMapper
{
    public static OcrProviderConfigResponse Map(OcrProviderConfigRecord record) =>
        new(
            record.Id,
            record.ProviderKey,
            record.Name,
            record.BaseUrl,
            !string.IsNullOrWhiteSpace(record.ApiKeyProtected),
            record.IsEnabled,
            record.LastTestedAtUtc,
            record.LastTestStatusKey,
            record.LastTestMessage,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.Version);
}
