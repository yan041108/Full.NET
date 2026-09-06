using Full.NET.Modules.Ocr.Contracts;
using Full.NET.Modules.Ocr.Persistence;

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
