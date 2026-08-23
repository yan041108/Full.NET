namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// Host 菜单类型稳定机器码；button 仅作为客户端虚拟操作项，不对应独立路由。
/// </summary>
public static class IdentityHostMenuTypes
{
    /// <summary>目录节点，仅用于分组，不承载页面。</summary>
    public const string Directory = "directory";

    /// <summary>菜单节点，对应一个可访问的页面路由。</summary>
    public const string Menu = "menu";

    /// <summary>按钮节点，仅作为页面内虚拟操作项，由客户端权限码控制可见性。</summary>
    public const string Button = "button";
}
