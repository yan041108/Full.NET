using System.Text.Json;

namespace Full.NET.Modules.Notifications.Contracts;

/// <summary>闭合 ProviderType 目录项；由代码拥有，禁止反射扫描未知程序集。</summary>
public sealed record NotificationProviderTypeDescriptor(
    string ProviderTypeKey,
    string AdapterVersion,
    IReadOnlyList<string> SupportedChannelKeys,
    IReadOnlyList<NotificationProviderConfigField> NonSecretFields,
    IReadOnlyList<string> SecretFieldKeys,
    bool SupportsNativeAot,
    string ReceiptModeKey);

/// <summary>受控非密钥配置字段；未知字段必须失败关闭。</summary>
public sealed record NotificationProviderConfigField(
    string Name,
    string TypeKey,
    bool Required);

/// <summary>创建渠道配置草稿；不接受明文 Secret。</summary>
public sealed record CreateNotificationProviderProfileRequest(
    string ProfileKey,
    string ProviderTypeKey,
    JsonElement NonSecretConfig,
    string? SecretReference);

/// <summary>
/// 更新草稿非密钥配置与 Secret Reference；<c>Version</c> 为 CAS 期望值。
/// <c>SecretReference</c> 为 <see langword="null"/> 时保留现值，空字符串用于显式清除。
/// </summary>
public sealed record UpdateNotificationProviderProfileRequest(
    JsonElement NonSecretConfig,
    string? SecretReference,
    long Version);

/// <summary>发布不可变 Profile 版本。</summary>
public sealed record PublishNotificationProviderProfileRequest(long Version);

/// <summary>启用或停用渠道配置；停用只阻止新路由，不排空在途 Delivery。</summary>
public sealed record SetNotificationProviderProfileEnabledRequest(long Version);

/// <summary>渠道配置详情；密钥只返回配置状态，永不回显 Reference 或明文。</summary>
public sealed record NotificationProviderProfileResponse(
    Guid Id,
    string ProfileKey,
    string ProviderTypeKey,
    string NonSecretConfigJson,
    string SecretStatus,
    bool IsEnabled,
    long DraftRevision,
    Guid? LatestPublishedVersionId,
    int? LatestPublishedVersionNumber,
    string? LatestAdapterVersion,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    long Version);
