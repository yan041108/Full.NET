namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 提供跨模块一致的动态权限策略名称。
/// </summary>
public static class FullNetPermissionPolicies
{
    private const string Prefix = "FullNET.Permission:";

    /// <summary>
    /// 为代码目录中的稳定权限码生成授权策略名称。
    /// </summary>
    /// <param name="permissionCode">代码目录中的稳定权限码。</param>
    /// <returns>可交给 ASP.NET Core Authorization 的策略名称。</returns>
    public static string For(string permissionCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permissionCode);
        return $"{Prefix}{permissionCode}";
    }

    /// <summary>
    /// 尝试从策略名称中读取权限码。
    /// </summary>
    /// <param name="policyName">待解析的策略名称。</param>
    /// <param name="permissionCode">解析成功时返回权限码。</param>
    /// <returns>名称是否属于 Full.NET 动态权限策略。</returns>
    public static bool TryRead(string policyName, out string permissionCode)
    {
        if (policyName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            permissionCode = policyName[Prefix.Length..];
            return permissionCode.Length > 0;
        }

        permissionCode = string.Empty;
        return false;
    }
}
