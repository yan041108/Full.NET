using Microsoft.AspNetCore.Authorization;

namespace Full.NET.Modules.Identity.Authorization;

internal sealed class FullNetPermissionRequirement(string permissionCode)
    : IAuthorizationRequirement
{
    public string PermissionCode { get; } = permissionCode;
}
