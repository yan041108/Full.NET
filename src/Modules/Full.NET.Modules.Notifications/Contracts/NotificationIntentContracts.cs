using System.Text.Json;

namespace Full.NET.Modules.Notifications.Contracts;

/// <summary>创建通知意图；TenantId 不得出现在请求体，幂等键不含 SceneKey。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。ProducerKey、SceneKey、TemplateKey、IdempotencyKey 等稳定键发布后不得改名或删除。</remarks>
/// <param name="ProducerKey">稳定生产者键，用于来源归属与配额。</param>
/// <param name="SceneKey">稳定场景键，用于路由与策略匹配；不参与幂等键。</param>
/// <param name="TemplateKey">稳定模板键，用于解析模板版本。</param>
/// <param name="Recipients">收件人输入集合；当前仅支持 <c>user</c> 类型。</param>
/// <param name="Parameters">模板参数 JSON；键名必须命中模板声明，未知键失败关闭。</param>
/// <param name="IdempotencyKey">调用方幂等键；同一 Producer + IdempotencyKey 重放返回同一 IntentId。</param>
/// <param name="AttachmentFileIds">附件文件 Id 集合；为 <see langword="null"/> 表示无附件。</param>
public sealed record CreateNotificationIntentRequest(
    string ProducerKey,
    string SceneKey,
    string TemplateKey,
    IReadOnlyList<NotificationRecipientInput> Recipients,
    JsonElement Parameters,
    string IdempotencyKey,
    IReadOnlyList<Guid>? AttachmentFileIds = null);

/// <summary>收件人输入；本切片仅支持 <c>user</c>，RecipientKey 为用户 Id 的 32 位十六进制。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。RecipientTypeKey 闭合类别发布后不得改名或删除。</remarks>
/// <param name="RecipientTypeKey">稳定收件人类型键；当前仅支持 <c>user</c>，未知值失败关闭。</param>
/// <param name="RecipientKey">收件人稳定键；为 <c>user</c> 类型时为用户 Id 的 32 位十六进制。</param>
public sealed record NotificationRecipientInput(
    string RecipientTypeKey,
    string RecipientKey);

/// <summary>意图受理结果；幂等回放返回同一 Id 与同一 Recipient 集合。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。ProducerKey、SceneKey、IdempotencyKey、PolicyCategoryKey、DispatchModeKey、StatusKey 等稳定键发布后不得改名或删除。</remarks>
/// <param name="Id">意图唯一标识；幂等回放返回同一值。</param>
/// <param name="ProducerKey">稳定生产者键，用于来源归属。</param>
/// <param name="SceneKey">稳定场景键，用于回溯路由。</param>
/// <param name="IdempotencyKey">调用方幂等键；同一 Producer + IdempotencyKey 回放返回同一 IntentId。</param>
/// <param name="TemplateVersionId">意图绑定的模板发布版本 Id；保证渲染一致。</param>
/// <param name="BindingVersionId">意图绑定的收件渠道绑定版本 Id；未绑定时为 <see langword="null"/>。</param>
/// <param name="PolicyCategoryKey">稳定策略类别键，用于路由与限流。</param>
/// <param name="DispatchModeKey">稳定分发模式键（如 immediate、batched）。</param>
/// <param name="StatusKey">稳定意图状态键，状态机迁移不可逆。</param>
/// <param name="RouteSnapshotJson">路由快照 JSON 文本；记录受理时刻的渠道与配置决策。</param>
/// <param name="ParameterSnapshotJson">参数快照 JSON 文本；记录受理时刻的模板入参，不可重放修改。</param>
/// <param name="Recipients">已解析收件人快照集合；幂等回放返回同一集合。</param>
/// <param name="Attachments">已绑定附件元数据集合；不含文件内容。</param>
/// <param name="CreatedAtUtc">意图受理时间（UTC）。</param>
public sealed record NotificationIntentResponse(
    Guid Id,
    string ProducerKey,
    string SceneKey,
    string IdempotencyKey,
    Guid TemplateVersionId,
    Guid? BindingVersionId,
    string PolicyCategoryKey,
    string DispatchModeKey,
    string StatusKey,
    string RouteSnapshotJson,
    string ParameterSnapshotJson,
    IReadOnlyList<NotificationRecipientResponse> Recipients,
    IReadOnlyList<NotificationIntentAttachmentResponse> Attachments,
    DateTimeOffset CreatedAtUtc);

/// <summary>Intent 已绑定的邮件附件元数据；不包含文件内容。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="FileId">附件文件 Id；指向已上传的持久化文件。</param>
/// <param name="SortOrder">附件展示顺序序号，从 0 起递增。</param>
public sealed record NotificationIntentAttachmentResponse(
    Guid FileId,
    int SortOrder);

/// <summary>已解析收件人快照；不回显地址原文。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。RecipientTypeKey、ResolutionStatusKey 等稳定键发布后不得改名或删除。</remarks>
/// <param name="Id">收件人快照记录唯一标识。</param>
/// <param name="RecipientTypeKey">稳定收件人类型键（如 user）。</param>
/// <param name="RecipientKey">收件人稳定键；与请求输入一致。</param>
/// <param name="UserId">解析后的用户 Id；未解析或非用户类型时为 <see langword="null"/>。</param>
/// <param name="ResolutionStatusKey">稳定解析状态键（如 resolved、invalid、suppressed）；用于路由与抑制决策。</param>
public sealed record NotificationRecipientResponse(
    Guid Id,
    string RecipientTypeKey,
    string RecipientKey,
    Guid? UserId,
    string ResolutionStatusKey);
