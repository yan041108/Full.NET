namespace Full.NET.Modules.Identity.Contracts;

/// <summary>OIDC 授权授予管理 API 契约。</summary>
/// <remarks>权限码字符串发布后不可改名或删除；新增权限只能追加，已发布权限码不得调整顺序。</remarks>
public static class IdentityOidcAuthorizationPermissions
{
    /// <summary>读取 OIDC 授权授予列表与详情。</summary>
    public const string Read = "identity.oidc_authorizations.read";

    /// <summary>吊销指定 OIDC 授权授予。</summary>
    public const string Revoke = "identity.oidc_authorizations.revoke";
}

/// <summary>OIDC 授权授予详情响应。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Id">授权授予唯一标识。</param>
/// <param name="ApplicationId">关联的应用 Id；机器码应用为 null。</param>
/// <param name="ClientId">OIDC 客户端标识；机器码客户端为 null。</param>
/// <param name="Subject">资源所有者（用户）标识；客户端凭据流为 null。</param>
/// <param name="Scopes">本次授权授予的 scope 列表。</param>
/// <param name="Status">授权状态字符串；稳定机器码，发布后不可改名。</param>
/// <param name="Type">授权类型字符串；稳定机器码，发布后不可改名。</param>
/// <param name="CreationDateUtc">OpenIddict 原始创建时间（UTC）；可能为 null。</param>
/// <param name="CreatedAtUtc">系统记录的创建时间（UTC）。</param>
/// <param name="Version">乐观并发版本号。</param>
public sealed record OidcAuthorizationResponse(
    Guid Id,
    Guid? ApplicationId,
    string? ClientId,
    string? Subject,
    IReadOnlyList<string> Scopes,
    string Status,
    string Type,
    DateTimeOffset? CreationDateUtc,
    DateTimeOffset CreatedAtUtc,
    int Version);