using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Security;

namespace Full.NET.Modules.Identity.Authorization;

/// <summary>
/// 自定义 IAuthorizationPolicyProvider，把 FullNetPermissionPolicies.For(code) 格式的策略名
/// 动态构造成基于 FullNetPermissionRequirement 的 AuthorizationPolicy；未知策略名回退到默认实现。
/// OpenAccess 前缀策略额外包含 SignatureAuthentication 方案，供对外开放的签名认证端点使用。
/// </summary>
internal sealed class FullNetPermissionPolicyProvider : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback;
    private readonly HashSet<string> _knownPermissions;

    public FullNetPermissionPolicyProvider(
        IOptions<AuthorizationOptions> options,
        AuthorizationCatalog catalog)
    {
        _fallback = new DefaultAuthorizationPolicyProvider(options);
        _knownPermissions = catalog.Permissions
            .Select(permission => permission.Code)
            .ToHashSet(StringComparer.Ordinal);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() =>
        _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() =>
        _fallback.GetFallbackPolicyAsync();

    /// <summary>
    /// 解析策略名并构造授权策略：FullNet.OpenAccess:xxx 前缀允许 API Key + 签名认证，
    /// FullNet.Permission:xxx 仅要求已认证用户 + 指定权限码。未知权限码返回 null 触发
    /// ASP.NET Core 的策略未找到错误，避免误放行。
    /// </summary>
    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (TryReadOpenAccessPolicy(policyName, out var openAccessPermission))
        {
            return Task.FromResult<AuthorizationPolicy?>(
                BuildPermissionPolicy(
                    openAccessPermission,
                    includeSignatureAuthentication: true));
        }

        if (!FullNetPermissionPolicies.TryRead(policyName, out var permissionCode))
        {
            return _fallback.GetPolicyAsync(policyName);
        }

        if (!_knownPermissions.Contains(permissionCode))
        {
            return Task.FromResult<AuthorizationPolicy?>(null);
        }

        return Task.FromResult<AuthorizationPolicy?>(
            BuildPermissionPolicy(permissionCode, includeSignatureAuthentication: false));
    }

    /// <summary>基于权限码构造标准权限策略名，供 [Authorize(Policy = ...)] 使用。</summary>
    public static string CreatePolicyName(string permissionCode) =>
        FullNetPermissionPolicies.For(permissionCode);

    /// <summary>构造包含签名认证方案的 OpenAccess 权限策略名。</summary>
    public static string CreateOpenAccessPolicyName(string permissionCode) =>
        $"FullNet.OpenAccess:{permissionCode}";

    private static bool TryReadOpenAccessPolicy(
        string policyName,
        out string permissionCode)
    {
        const string prefix = "FullNet.OpenAccess:";
        if (policyName.StartsWith(prefix, StringComparison.Ordinal))
        {
            permissionCode = policyName[prefix.Length..];
            return permissionCode.Length > 0;
        }

        permissionCode = string.Empty;
        return false;
    }

    private AuthorizationPolicy BuildPermissionPolicy(
        string permissionCode,
        bool includeSignatureAuthentication)
    {
        var builder = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new FullNetPermissionRequirement(permissionCode));
        if (includeSignatureAuthentication)
        {
            builder.AddAuthenticationSchemes(
                SmartAuthenticationDefaults.AuthenticationScheme,
                SignatureAuthenticationDefaults.AuthenticationScheme);
        }

        return builder.Build();
    }
}
