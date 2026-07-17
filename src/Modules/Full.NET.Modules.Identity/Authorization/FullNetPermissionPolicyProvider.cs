using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Full.NET.Modules.Identity.Contracts;

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
        if (!FullNetPermissionPolicies.TryRead(policyName, out var permissionCode))
        {
            return _fallback.GetPolicyAsync(policyName);
        }

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
        return FullNetPermissionPolicies.For(permissionCode);
    }
}
