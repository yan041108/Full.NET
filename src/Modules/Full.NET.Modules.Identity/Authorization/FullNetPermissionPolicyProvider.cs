using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Security;

namespace Full.NET.Modules.Identity.Authorization;

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

    public static string CreatePolicyName(string permissionCode) =>
        FullNetPermissionPolicies.For(permissionCode);

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
