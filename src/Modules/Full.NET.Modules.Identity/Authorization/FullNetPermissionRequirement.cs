using Microsoft.AspNetCore.Authorization;

/// <summary>ASP.NET Core 授权要求；与策略组合判定权限。</summary>
namespace Full.NET.Modules.Identity.Authorization;

internal sealed class FullNetPermissionRequirement(string permissionCode)
    : IAuthorizationRequirement
{
    public string PermissionCode { get; } = permissionCode;
}
