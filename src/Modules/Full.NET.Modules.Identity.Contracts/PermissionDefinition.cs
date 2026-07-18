namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 表示权限可应用的业务作用域。
/// </summary>
[Flags]
public enum AuthorizationScope
{
    /// <summary>
    /// 权限可用于宿主控制面。
    /// </summary>
    Host = 1,

    /// <summary>
    /// 权限可用于租户业务域。
    /// </summary>
    Tenant = 2,
}

/// <summary>
/// 定义由代码拥有的稳定权限。
/// </summary>
/// <param name="Code">跨版本保持稳定的英文权限码。</param>
/// <param name="Name">供管理界面展示的中文名称。</param>
/// <param name="Scope">权限允许出现的业务作用域。</param>
public sealed record PermissionDefinition(
    string Code,
    string Name,
    AuthorizationScope Scope);
