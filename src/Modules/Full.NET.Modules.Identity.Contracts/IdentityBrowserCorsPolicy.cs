namespace Full.NET.Modules.Identity.Contracts;

/// <summary>身份模块对浏览器暴露的精确来源策略名称。</summary>
public static class IdentityBrowserCorsPolicy
{
    /// <summary>浏览器客户端来源策略；来源集合由宿主的身份配置提供。</summary>
    public const string Name = "FullNET.Identity.BrowserClients";
}
