namespace Full.NET.Modules.Webhooks.Contracts;

/// <summary>固定一次准备意图；秘密只写入，不进入回执。</summary>
/// <param name="OperationId">未知结果重读或重试必须沿用的原操作。</param>
/// <param name="Version">原订阅修订。</param>
/// <param name="SigningSecret">接收方受保护配置中的新随机秘密。</param>
public sealed record PrepareWebhookSigningKeyRotationRequest(Guid OperationId, int Version, string SigningSecret);

/// <summary>操作者明确核对当前修订及准备的公开密钥身份后提交。</summary>
/// <param name="Version">本次确认所依据的订阅修订。</param>
/// <param name="SigningKeyId">准备阶段返回并已核对的公开身份。</param>
public sealed record CompleteWebhookSigningKeyRotationRequest(int Version, Guid SigningKeyId);

/// <summary>轮换状态及本地领域审计回执，不包含秘密、指纹或操作者会话。</summary>
/// <param name="OperationId">固定的原操作。</param>
/// <param name="TenantId">可信企业。</param>
/// <param name="SubscriptionId">原订阅。</param>
/// <param name="PreviousKeyId">准备时仍活动的旧身份。</param>
/// <param name="SigningKeyId">准备的新公开身份。</param>
/// <param name="PreviousVersion">准备前订阅修订。</param>
/// <param name="PreparedVersion">准备成功后的订阅修订。</param>
/// <param name="StateKey">prepared、activated 或 cancelled。</param>
/// <param name="PreparedAtUtc">准备提交的 UTC 时间。</param>
/// <param name="CompletedAtUtc">明确激活或取消的 UTC 时间。</param>
/// <param name="CompletedVersion">完成时订阅修订；以后更改不重写此事实。</param>
public sealed record WebhookSigningKeyRotationReceipt(Guid OperationId, Guid TenantId, Guid SubscriptionId, Guid PreviousKeyId,
    Guid SigningKeyId, int PreviousVersion, int PreparedVersion, string StateKey, DateTimeOffset PreparedAtUtc,
    DateTimeOffset? CompletedAtUtc, int? CompletedVersion);
