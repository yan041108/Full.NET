using System.Text.Json;

namespace Full.NET.Modules.Notifications.Contracts;

/// <summary>闭合 ProviderType 目录项；由代码拥有，禁止反射扫描未知程序集。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。ProviderTypeKey、ReceiptModeKey 等稳定键发布后不得改名或删除。</remarks>
/// <param name="ProviderTypeKey">稳定 Provider 类型键，用于路由与适配器选择。</param>
/// <param name="AdapterVersion">适配器版本号，用于运行时兼容判断与回退门禁。</param>
/// <param name="SupportedChannelKeys">该 Provider 支持的稳定渠道键集合（如 email、sms）。</param>
/// <param name="NonSecretFields">受控非密钥配置字段集合；未知字段必须失败关闭。</param>
/// <param name="SecretFieldKeys">密钥字段键集合；只描述键名，永不返回明文或 Reference。</param>
/// <param name="SupportsNativeAot">是否兼容 Native AOT；用于裁剪与源生成约束判断。</param>
/// <param name="ReceiptModeKey">稳定回执模式键（如 webhook、polling）；用于选择回执处理路径。</param>
public sealed record NotificationProviderTypeDescriptor(
    string ProviderTypeKey,
    string AdapterVersion,
    IReadOnlyList<string> SupportedChannelKeys,
    IReadOnlyList<NotificationProviderConfigField> NonSecretFields,
    IReadOnlyList<string> SecretFieldKeys,
    bool SupportsNativeAot,
    string ReceiptModeKey);

/// <summary>受控非密钥配置字段；未知字段必须失败关闭。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。TypeKey 闭合类别发布后不得改名。</remarks>
/// <param name="Name">字段名；发布后稳定，未知字段必须失败关闭而非忽略。</param>
/// <param name="TypeKey">闭合字段类型键（如 string、int、bool、json）。</param>
/// <param name="Required">是否必填；为 <see langword="true"/> 时缺失即拒绝。</param>
public sealed record NotificationProviderConfigField(
    string Name,
    string TypeKey,
    bool Required);

/// <summary>创建渠道配置草稿；不接受明文 Secret。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。ProfileKey、ProviderTypeKey 稳定键发布后不得改名或删除。</remarks>
/// <param name="ProfileKey">稳定 Profile 键，业务路由据此选择配置；发布后不可改名。</param>
/// <param name="ProviderTypeKey">稳定 Provider 类型键，必须命中 <see cref="NotificationProviderTypeDescriptor"/> 闭合目录。</param>
/// <param name="NonSecretConfig">非密钥配置 JSON；字段必须命中 Provider 声明的 <see cref="NotificationProviderConfigField"/>，未知字段失败关闭。</param>
/// <param name="SecretReference">密钥引用句柄（如外部密钥管理服务的引用 Id）；为 <see langword="null"/> 表示无密钥，禁止传入明文。</param>
public sealed record CreateNotificationProviderProfileRequest(
    string ProfileKey,
    string ProviderTypeKey,
    JsonElement NonSecretConfig,
    string? SecretReference);

/// <summary>
/// 更新草稿非密钥配置与 Secret Reference；<c>Version</c> 为 CAS 期望值。
/// <c>SecretReference</c> 为 <see langword="null"/> 时保留现值，空字符串用于显式清除。
/// </summary>
/// <param name="NonSecretConfig">非密钥配置 JSON；字段必须命中 Provider 声明的 <see cref="NotificationProviderConfigField"/>，未知字段失败关闭。</param>
/// <param name="SecretReference">密钥引用句柄；为 <see langword="null"/> 时保留现值，空字符串用于显式清除，禁止传入明文。</param>
/// <param name="Version">CAS 乐观并发期望值；不匹配当前草稿 Revision 时拒绝。</param>
public sealed record UpdateNotificationProviderProfileRequest(
    JsonElement NonSecretConfig,
    string? SecretReference,
    long Version);

/// <summary>发布不可变 Profile 版本。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。已发布版本不可修改或回收。</remarks>
/// <param name="Version">CAS 乐观并发期望值；不匹配当前草稿 Revision 时拒绝发布。</param>
public sealed record PublishNotificationProviderProfileRequest(long Version);

/// <summary>启用或停用渠道配置；停用只阻止新路由，不排空在途 Delivery。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Version">CAS 乐观并发期望值；不匹配当前草稿 Revision 时拒绝切换。</param>
public sealed record SetNotificationProviderProfileEnabledRequest(long Version);

/// <summary>渠道配置详情；密钥只返回配置状态，永不回显 Reference 或明文。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。ProfileKey、ProviderTypeKey、SecretStatus 等稳定键发布后不得改名或删除。</remarks>
/// <param name="Id">Profile 记录唯一标识。</param>
/// <param name="ProfileKey">稳定 Profile 键，业务路由据此选择配置。</param>
/// <param name="ProviderTypeKey">稳定 Provider 类型键，选择适配器。</param>
/// <param name="NonSecretConfigJson">非密钥配置 JSON 文本；由服务端序列化，不接受外部反序列化结果。</param>
/// <param name="SecretStatus">稳定密钥状态键（如 configured、missing）；只描述状态，不回显 Reference 或明文。</param>
/// <param name="IsEnabled">是否启用；停用只阻止新路由，不排空在途 Delivery。</param>
/// <param name="DraftRevision">草稿 CAS 修订号；用于乐观并发与发布对账。</param>
/// <param name="LatestPublishedVersionId">最近发布版本 Id；从未发布时为 <see langword="null"/>。</param>
/// <param name="LatestPublishedVersionNumber">最近发布版本号；从未发布时为 <see langword="null"/>。</param>
/// <param name="LatestAdapterVersion">最近发布所用的适配器版本号；用于运行时兼容判断。</param>
/// <param name="CreatedAtUtc">Profile 创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">Profile 最近更新时间（UTC）；未更新时为 <see langword="null"/>。</param>
/// <param name="Version">对外暴露的 CAS 期望值；与 <paramref name="DraftRevision"/> 同步递增。</param>
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
