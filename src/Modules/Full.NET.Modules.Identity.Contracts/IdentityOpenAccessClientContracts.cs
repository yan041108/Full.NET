namespace Full.NET.Modules.Identity.Contracts;

/// <summary>OpenAccess 接入方应用管理 API 契约。</summary>
public static class IdentityOpenAccessClientPermissions
{
    /// <summary>分页查询 OpenAccess 接入方应用。</summary>
    public const string Read = "identity.open_access_clients.read";

    /// <summary>创建 OpenAccess 接入方应用。</summary>
    public const string Create = "identity.open_access_clients.create";

    /// <summary>更新 OpenAccess 接入方应用元数据与权限。</summary>
    public const string Update = "identity.open_access_clients.update";

    /// <summary>停用 OpenAccess 接入方应用。</summary>
    public const string Disable = "identity.open_access_clients.disable";

    /// <summary>轮换 OpenAccess 接入方应用密钥。</summary>
    public const string Rotate = "identity.open_access_clients.rotate";

    /// <summary>在受控边界内调试接入方 HMAC 签名构造。</summary>
    public const string DebugSignature = "identity.open_access_clients.debug_signature";
}

/// <summary>OpenAccess 接入方认证审计事件类型常量。</summary>
public static class IdentityOpenAccessClientAuditEventTypes
{
    /// <summary>HMAC 签名认证尝试。</summary>
    public const string SignatureAuthentication = "signature_authentication";

    /// <summary>ApiKey Header 认证尝试（仅限接入方应用）。</summary>
    public const string ApiKeyAuthentication = "open_access_api_key_authentication";
}

/// <summary>创建 OpenAccess 接入方应用请求。</summary>
/// <param name="UserId">绑定的 Host 用户标识；该用户必须为活动账号。</param>
/// <param name="Name">接入方应用名称。</param>
/// <param name="Description">接入方应用描述；可为空。</param>
/// <param name="Remark">管理员备注；可为空。</param>
/// <param name="Permissions">该应用被授予的稳定权限码集合；必须为 Host 作用域内已发布权限。</param>
/// <param name="ExpiresAtUtc">可选的过期时间；<see langword="null"/> 表示长期有效（仍可被主动停用或轮换）。</param>
/// <param name="DailyRequestQuota">可选的每日成功认证配额；<see langword="null"/> 表示不限。</param>
public sealed record CreateOpenAccessClientRequest(
    Guid UserId,
    string Name,
    string? Description,
    string? Remark,
    IReadOnlyList<string> Permissions,
    DateTimeOffset? ExpiresAtUtc,
    int? DailyRequestQuota = null);

/// <summary>更新 OpenAccess 接入方应用请求。</summary>
/// <param name="Name">接入方应用名称。</param>
/// <param name="Description">接入方应用描述；可为空。</param>
/// <param name="Remark">管理员备注；可为空。</param>
/// <param name="Permissions">该应用被授予的稳定权限码集合。</param>
/// <param name="ExpiresAtUtc">可选的过期时间；<see langword="null"/> 表示长期有效。</param>
/// <param name="DailyRequestQuota">可选的每日成功认证配额；<see langword="null"/> 表示不限。</param>
/// <param name="Version">客户端感知的乐观并发版本号。</param>
public sealed record UpdateOpenAccessClientRequest(
    string Name,
    string? Description,
    string? Remark,
    IReadOnlyList<string> Permissions,
    DateTimeOffset? ExpiresAtUtc,
    int? DailyRequestQuota,
    int Version);

/// <summary>OpenAccess 接入方应用响应（不含明文密钥）。</summary>
/// <param name="Id">接入方应用稳定标识。</param>
/// <param name="ApiKeyId">当前绑定的 API Key 标识。</param>
/// <param name="UserId">绑定的 Host 用户标识。</param>
/// <param name="Username">绑定的 Host 用户登录名。</param>
/// <param name="Name">接入方应用名称。</param>
/// <param name="Description">接入方应用描述。</param>
/// <param name="Remark">管理员备注。</param>
/// <param name="AccessKeyId">公开 Access Key Id；等于 API Key 的 KeyPrefix，用于 HMAC 签名认证。</param>
/// <param name="Permissions">该应用被授予的稳定权限码集合。</param>
/// <param name="ExpiresAtUtc">过期时间；<see langword="null"/> 表示长期有效。</param>
/// <param name="IsActive">是否处于活动状态；停用后密钥不再通过认证。</param>
/// <param name="DailyRequestQuota">每日成功认证配额；<see langword="null"/> 表示不限。</param>
/// <param name="LastUsedAtUtc">最近一次通过认证的时间（UTC）；从未使用时为 <see langword="null"/>。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="Version">乐观并发版本号。</param>
public sealed record OpenAccessClientResponse(
    Guid Id,
    Guid ApiKeyId,
    Guid UserId,
    string Username,
    string Name,
    string? Description,
    string? Remark,
    string AccessKeyId,
    IReadOnlyList<string> Permissions,
    DateTimeOffset? ExpiresAtUtc,
    int? DailyRequestQuota,
    bool IsActive,
    DateTimeOffset? LastUsedAtUtc,
    DateTimeOffset CreatedAtUtc,
    int Version);

/// <summary>创建或轮换 OpenAccess 接入方应用成功响应；明文密钥只返回一次。</summary>
/// <param name="Client">不含明文密钥的应用投影。</param>
/// <param name="Secret">一次性返回的明文密钥；调用方必须立即写入安全 Secret Store，禁止落盘或写日志。</param>
public sealed record CreateOpenAccessClientResponse(
    OpenAccessClientResponse Client,
    string Secret);

/// <summary>接入方应用访问审计条目；不含密钥、签名或请求体。</summary>
/// <param name="Id">审计事件标识。</param>
/// <param name="EventType">事件类型。</param>
/// <param name="ResultCode">稳定结果码。</param>
/// <param name="Succeeded">是否成功。</param>
/// <param name="IpAddress">来源 IP。</param>
/// <param name="UserAgent">来源 User-Agent 摘要。</param>
/// <param name="OccurredAtUtc">发生时间（UTC）。</param>
public sealed record OpenAccessClientAccessLogEntry(
    Guid Id,
    string EventType,
    string ResultCode,
    bool Succeeded,
    string? IpAddress,
    string? UserAgent,
    DateTimeOffset OccurredAtUtc);

/// <summary>接入方应用当日用量与配额投影。</summary>
/// <param name="ClientId">接入方应用标识。</param>
/// <param name="DailyRequestQuota">每日成功认证配额；<see langword="null"/> 表示不限。</param>
/// <param name="TodaySuccessCount">当前 UTC 日成功认证次数。</param>
/// <param name="TodayFailureCount">当前 UTC 日失败认证次数。</param>
/// <param name="WindowStartUtc">统计窗口起始（UTC 日界）。</param>
/// <param name="WindowEndUtc">统计窗口结束（下一 UTC 日界）。</param>
/// <param name="QuotaExceeded">当前成功次数是否已达到或超过配额。</param>
public sealed record OpenAccessClientUsageResponse(
    Guid ClientId,
    int? DailyRequestQuota,
    int TodaySuccessCount,
    int TodayFailureCount,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    bool QuotaExceeded);

/// <summary>有界签名调试请求；操作者粘贴客户端侧材料，服务端不回显存储密钥。</summary>
/// <param name="Secret">客户端持有的明文密钥；仅用于本次验算，禁止持久化。</param>
/// <param name="Method">HTTP 方法。</param>
/// <param name="Path">请求路径（含 PathBase）。</param>
/// <param name="Query">原始查询字符串（可含或不含前导 ?）。</param>
/// <param name="BodyBase64">请求体 Base64；空体传 <see langword="null"/> 或空字符串。</param>
/// <param name="Timestamp">Unix 秒时间戳字符串。</param>
/// <param name="Nonce">随机 Nonce。</param>
/// <param name="Signature">客户端计算的签名十六进制小写串。</param>
/// <param name="SignatureVersion">签名协议版本。</param>
public sealed record OpenAccessClientSignatureDebugRequest(
    string Secret,
    string Method,
    string Path,
    string? Query,
    string? BodyBase64,
    string Timestamp,
    string Nonce,
    string Signature,
    string SignatureVersion);

/// <summary>有界签名调试响应；只返回验算材料，不回显服务器存储密钥。</summary>
/// <param name="SignaturesMatch">提供签名是否与按请求材料计算的期望签名一致。</param>
/// <param name="CanonicalString">规范化签名字符串。</param>
/// <param name="ContentHash">请求体 SHA-256 十六进制小写摘要。</param>
/// <param name="ExpectedSignature">基于请求中 Secret 计算的期望签名。</param>
/// <param name="ProvidedSignature">请求中提供的签名（规范化后）。</param>
/// <param name="Diagnostics">逐步诊断说明。</param>
public sealed record OpenAccessClientSignatureDebugResponse(
    bool SignaturesMatch,
    string CanonicalString,
    string ContentHash,
    string ExpectedSignature,
    string ProvidedSignature,
    IReadOnlyList<string> Diagnostics);
