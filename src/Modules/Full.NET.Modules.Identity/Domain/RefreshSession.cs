namespace Full.NET.Modules.Identity.Domain;

/// <summary>
/// Refresh Session 聚合根（不可变记录）。代表一次登录后产生的可轮换 Refresh Token。
/// 不变量：
/// - FamilyId 用于一次性撤销整族轮换链；检测到 Replay 时整族撤销。
/// - TokenHash 是明文 Refresh Token 的 SHA-256；从不保留明文。
/// - ConsumedAtUtc 被设置后不可再次轮换，后续同 Token 提交视为 Replay。
/// - Version 字段用于 Refresh 消耗时的乐观并发，防止双重轮换竞态。
/// </summary>
/// <param name="Id">Session 唯一 ID，写入 JWT SessionId Claim。</param>
/// <param name="UserId">所属用户。</param>
/// <param name="FamilyId">Session 家族 ID；整族批量吊销时使用。</param>
/// <param name="ClientId">创建会话的 OAuth 客户端标识。</param>
/// <param name="TokenHash">Refresh Token 明文的 SHA-256 哈希。</param>
/// <param name="ExpiresAtUtc">Refresh Session 过期时间 UTC。</param>
/// <param name="ConsumedAtUtc">被轮换使用时间；空表示未使用可继续轮换。</param>
/// <param name="RevokedAtUtc">整族吊销时间；登出/超管撤销/Replay 检测时写入。</param>
/// <param name="ReplacedById">成功轮换后指向新 Session Id，便于追踪链路。</param>
/// <param name="ActiveTenantId">当前活动租户上下文；Host 会话可切换进入。</param>
/// <param name="CreatedAtUtc">创建时间 UTC。</param>
/// <param name="Version">乐观并发版本号；消耗时用于 CAS 更新。</param>
internal sealed record RefreshSession(
    Guid Id,
    Guid UserId,
    Guid FamilyId,
    string ClientId,
    string TokenHash,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? ConsumedAtUtc,
    DateTimeOffset? RevokedAtUtc,
    Guid? ReplacedById,
    Guid? ActiveTenantId,
    DateTimeOffset CreatedAtUtc,
    int Version);
