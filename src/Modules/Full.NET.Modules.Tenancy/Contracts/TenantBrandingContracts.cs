namespace Full.NET.Modules.Tenancy.Contracts;

/// <summary>租户品牌与联系信息读写权限。</summary>
/// <remarks>权限码字符串发布后不可改名或删除；新增权限只能追加到本类末尾，避免破坏既有角色分配与策略缓存。</remarks>
public static class TenantBrandingPermissions
{
    /// <summary>读取当前作用域租户品牌信息。</summary>
    public const string Read = "tenancy.tenant_branding.read";

    /// <summary>更新当前作用域租户品牌信息。</summary>
    public const string Update = "tenancy.tenant_branding.update";
}

/// <summary>租户品牌与联系信息读取响应。</summary>
/// <remarks>
/// 字段顺序与命名为稳定机器码的一部分；发布后不可改名或删除，新增字段只能追加到末尾。
/// </remarks>
/// <param name="TenantId">租户标识。</param>
/// <param name="SystemTitle">系统展示标题。</param>
/// <param name="LogoFileId">Logo 文件标识；仅允许通过 Files 模块绑定。</param>
/// <param name="ContactPhone">联系电话。</param>
/// <param name="ContactEmail">联系邮箱。</param>
/// <param name="ContactAddress">联系地址。</param>
/// <param name="Copyright">版权信息。</param>
/// <param name="Version">乐观并发版本。</param>
public sealed record TenantBrandingResponse(
    Guid TenantId,
    string? SystemTitle,
    Guid? LogoFileId,
    string? ContactPhone,
    string? ContactEmail,
    string? ContactAddress,
    string? Copyright,
    int Version);

/// <summary>登录页与壳层消费的运行时品牌摘要；不包含内部版本号。</summary>
/// <remarks>
/// 字段顺序与命名为稳定机器码的一部分；发布后不可改名或删除，新增字段只能追加到末尾。
/// </remarks>
/// <param name="SystemTitle">系统展示标题。</param>
/// <param name="HasLogo">是否已绑定 Logo 文件。</param>
/// <param name="ContactPhone">联系电话。</param>
/// <param name="ContactEmail">联系邮箱。</param>
/// <param name="ContactAddress">联系地址。</param>
/// <param name="Copyright">版权信息。</param>
public sealed record TenantRuntimeBrandingResponse(
    string? SystemTitle,
    bool HasLogo,
    string? ContactPhone,
    string? ContactEmail,
    string? ContactAddress,
    string? Copyright);

/// <summary>更新租户品牌文本字段请求；Logo 仅能通过专用媒体端点绑定。</summary>
/// <remarks>
/// 字段顺序与命名为稳定机器码的一部分；发布后不可改名或删除，新增字段只能追加到末尾。
/// </remarks>
/// <param name="SystemTitle">系统展示标题。</param>
/// <param name="ContactPhone">联系电话。</param>
/// <param name="ContactEmail">联系邮箱。</param>
/// <param name="ContactAddress">联系地址。</param>
/// <param name="Copyright">版权信息。</param>
/// <param name="Version">调用方看到的当前版本。</param>
public sealed record UpdateTenantBrandingRequest(
    string? SystemTitle,
    string? ContactPhone,
    string? ContactEmail,
    string? ContactAddress,
    string? Copyright,
    int Version);
