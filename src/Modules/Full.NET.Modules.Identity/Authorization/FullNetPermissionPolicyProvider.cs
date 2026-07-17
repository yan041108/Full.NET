using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Authorization;

internal sealed class FullNetPermissionPolicyProvider : IAuthorizationPolicyProvider
{
    private const string PolicyPrefix = "FullNET.Permission:";
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
        if (!policyName.StartsWith(PolicyPrefix, StringComparison.Ordinal))
        {
            return _fallback.GetPolicyAsync(policyName);
        }

        var permissionCode = policyName[PolicyPrefix.Length..];
        if (!_knownPermissions.Contains(permissionCode))
        {
            return Task.FromResult<AuthorizationPolicy?>(null);
        }

        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new FullNetPermissionRequirement(permissionCode))
            .Build();
        return Task.FromResult<AuthorizationPolicy?>(policy);
    }

    public static string CreatePolicyName(string permissionCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permissionCode);
        return $"{PolicyPrefix}{permissionCode}";
    }
}
