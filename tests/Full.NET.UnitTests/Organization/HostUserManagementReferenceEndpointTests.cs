using System.Reflection;
using System.Security.Claims;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;

namespace Full.NET.UnitTests.Organization;

[TestClass]
public sealed class HostUserManagementReferenceEndpointTests
{
    private static readonly MethodInfo TryResolveReferenceAccessMethod =
        typeof(Full.NET.Modules.Organization.Features.HostUserManagementReference.Endpoint)
            .GetMethod(
                "TryResolveReferenceAccess",
                BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(
            "未找到 HostUserManagementReference.Endpoint.TryResolveReferenceAccess。");

    [TestMethod]
    public void TryResolveReferenceAccess_accepts_identity_user_update_for_host_directory()
    {
        var principal = CreatePrincipal(IdentityUserManagementPermissions.Update);

        var result = InvokeTryResolveReferenceAccess(
            principal,
            out var canAccessUserUnits,
            out var canAccessUserPositions);

        Assert.IsTrue(result);
        Assert.IsTrue(canAccessUserUnits);
        Assert.IsTrue(canAccessUserPositions);
    }

    [TestMethod]
    public void TryResolveReferenceAccess_accepts_identity_user_read_for_host_directory()
    {
        var principal = CreatePrincipal(IdentityUserManagementPermissions.Read);

        var result = InvokeTryResolveReferenceAccess(
            principal,
            out var canAccessUserUnits,
            out var canAccessUserPositions);

        Assert.IsTrue(result);
        Assert.IsTrue(canAccessUserUnits);
        Assert.IsTrue(canAccessUserPositions);
    }

    [TestMethod]
    public void TryResolveReferenceAccess_rejects_principal_without_directory_or_organization_permissions()
    {
        var principal = CreatePrincipal("settings.config.read");

        var result = InvokeTryResolveReferenceAccess(
            principal,
            out var canAccessUserUnits,
            out var canAccessUserPositions);

        Assert.IsFalse(result);
        Assert.IsFalse(canAccessUserUnits);
        Assert.IsFalse(canAccessUserPositions);
    }

    [TestMethod]
    public void TryResolveReferenceAccess_accepts_user_unit_permissions()
    {
        var principal = CreatePrincipal(
            OrganizationUserUnitManagementPermissions.Create);

        var result = InvokeTryResolveReferenceAccess(
            principal,
            out var canAccessUserUnits,
            out var canAccessUserPositions);

        Assert.IsTrue(result);
        Assert.IsTrue(canAccessUserUnits);
        Assert.IsFalse(canAccessUserPositions);
    }

    [TestMethod]
    public void TryResolveReferenceAccess_accepts_user_position_permissions()
    {
        var principal = CreatePrincipal(
            OrganizationUserPositionManagementPermissions.Disable);

        var result = InvokeTryResolveReferenceAccess(
            principal,
            out var canAccessUserUnits,
            out var canAccessUserPositions);

        Assert.IsTrue(result);
        Assert.IsFalse(canAccessUserUnits);
        Assert.IsTrue(canAccessUserPositions);
    }

    [TestMethod]
    public void TryResolveReferenceAccess_accepts_both_organization_permission_groups()
    {
        var principal = CreatePrincipal(
            IdentityUserManagementPermissions.Read,
            OrganizationUserUnitManagementPermissions.Update,
            OrganizationUserPositionManagementPermissions.Read);

        var result = InvokeTryResolveReferenceAccess(
            principal,
            out var canAccessUserUnits,
            out var canAccessUserPositions);

        Assert.IsTrue(result);
        Assert.IsTrue(canAccessUserUnits);
        Assert.IsTrue(canAccessUserPositions);
    }

    [TestMethod]
    public void TryResolveReferenceAccess_accepts_super_administrator_without_permission_claims()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(FullNetIdentityClaimTypes.Subject, Guid.NewGuid().ToString("D")),
            new Claim(FullNetIdentityClaimTypes.ActorScope, "host"),
            new Claim(FullNetIdentityClaimTypes.SuperAdministrator, bool.TrueString),
        ],
        "Test"));

        var result = InvokeTryResolveReferenceAccess(
            principal,
            out var canAccessUserUnits,
            out var canAccessUserPositions);

        Assert.IsTrue(result);
        Assert.IsTrue(canAccessUserUnits);
        Assert.IsTrue(canAccessUserPositions);
    }

    private static bool InvokeTryResolveReferenceAccess(
        ClaimsPrincipal principal,
        out bool canAccessUserUnits,
        out bool canAccessUserPositions)
    {
        object?[] args = [principal, null, null];
        var result = (bool)(TryResolveReferenceAccessMethod.Invoke(null, args)
            ?? throw new InvalidOperationException("权限解析返回了空结果。"));
        canAccessUserUnits = (bool)(args[1]
            ?? throw new InvalidOperationException("未返回用户机构访问标记。"));
        canAccessUserPositions = (bool)(args[2]
            ?? throw new InvalidOperationException("未返回用户职位访问标记。"));
        return result;
    }

    private static ClaimsPrincipal CreatePrincipal(params string[] permissionCodes)
    {
        var claims = new List<Claim>
        {
            new(FullNetIdentityClaimTypes.Subject, Guid.NewGuid().ToString("D")),
            new(FullNetIdentityClaimTypes.ActorScope, "host"),
        };
        claims.AddRange(permissionCodes.Select(permissionCode =>
            new Claim(FullNetIdentityClaimTypes.Permission, permissionCode)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }
}
