namespace Full.NET.Modules.Identity.Contracts;

/// <summary>OIDC 签名密钥管理 API 契约；仅暴露公钥元数据，永不返回私钥材料。</summary>
/// <remarks>权限码字符串发布后不可改名或删除；新增权限只能追加，已发布权限码不得调整顺序。私钥材料只存在于服务端，禁止通过任何契约回显。</remarks>
public static class IdentityOidcSigningKeyPermissions
{
    /// <summary>查询 OIDC 签名密钥列表与公钥元数据。</summary>
    public const string Read = "identity.oidc_signing_keys.read";

    /// <summary>激活指定 OIDC 签名密钥；激活后立即用于签发新的 ID Token。</summary>
    public const string Activate = "identity.oidc_signing_keys.activate";
    /// <summary>登记本节点已配置候选公钥及其预发布窗口。</summary>
    public const string Publish = "identity.oidc_signing_keys.publish";
    /// <summary>在覆盖合法令牌窗口后退役非活动签名材料。</summary>
    public const string Retire = "identity.oidc_signing_keys.retire";
}

/// <summary>OIDC 签名密钥投影；仅包含公钥元数据，永不返回私钥材料。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾。KeyId 与 Algorithm 发布后不可改名，以保证 Token 验证稳定性。</remarks>
/// <param name="KeyId">密钥稳定标识；对应 JWT kid Header。</param>
/// <param name="IsActive">是否为当前激活签名密钥；同一时刻仅一把密钥处于激活状态。</param>
/// <param name="HasPrivateKey">服务端是否持有私钥；为 <see langword="false"/> 时该密钥仅用于验证旧 Token。</param>
/// <param name="Algorithm">签名算法机器码（如 RS256）；发布后不可改名。</param>
/// <param name="PublicKeyPem">PEM 编码的公钥；用于客户端验证 Token 签名。</param>
/// <param name="LifecycleState">unregistered、prepublished、active、retained 或 retired。</param>
/// <param name="PublicKeyHash">绑定 kid 的规范公钥 SHA256 指纹。</param>
/// <param name="PublishedAtUtc">公钥预发布登记时间。</param>
/// <param name="RetireAfterUtc">覆盖全部合法协议令牌后的最早退役时间。</param>
/// <param name="IsActivationReady">当前公开材料、节点租约及预发布窗口是否满足激活门禁。</param>
/// <param name="IsRetirementReady">当前非活动材料是否已满足退役门禁。</param>
public sealed record OidcSigningKeyResponse(
    string KeyId,
    bool IsActive,
    bool HasPrivateKey,
    string Algorithm,
    string PublicKeyPem,
    string LifecycleState = "unregistered",
    string? PublicKeyHash = null,
    DateTimeOffset? PublishedAtUtc = null,
    DateTimeOffset? RetireAfterUtc = null,
    bool IsActivationReady = false,
    bool IsRetirementReady = false);

/// <summary>OIDC 签名密钥列表响应；包含激活密钥、开发占位标记与全部公钥元数据。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾。UsesEphemeralDevelopmentKey 为 <see langword="true"/> 时表示生产环境未配置正式密钥，禁止在外部依赖该标志做安全决策。</remarks>
/// <param name="ActiveSigningKeyId">当前激活签名密钥的稳定标识。</param>
/// <param name="UsesEphemeralDevelopmentKey">是否正在使用临时开发密钥；生产环境必须为 <see langword="false"/>。</param>
/// <param name="Keys">全部签名密钥公钥元数据集合；包含历史与当前激活密钥。</param>
public sealed record OidcSigningKeyListResponse(
    string ActiveSigningKeyId,
    bool UsesEphemeralDevelopmentKey,
    IReadOnlyList<OidcSigningKeyResponse> Keys);
