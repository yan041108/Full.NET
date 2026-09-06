using Full.NET.Modules.K3Cloud.Contracts;
using Full.NET.Modules.K3Cloud.Persistence;

namespace Full.NET.Modules.K3Cloud.Features.ManageConnectionConfigs;

/// <summary>K3Cloud 连接配置 DTO 映射。</summary>
internal static class K3CloudConnectionMapper
{
    public static K3CloudConnectionConfigResponse Map(K3CloudConnectionConfigRecord record) =>
        new(
            record.Id,
            record.Name,
            record.BaseUrl,
            record.AcctId,
            record.Username,
            record.Lcid,
            !string.IsNullOrWhiteSpace(record.PasswordProtected),
            record.IsDefault,
            record.IsEnabled,
            record.LastTestedAtUtc,
            record.LastTestStatusKey,
            record.LastTestMessage,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.Version);
}
