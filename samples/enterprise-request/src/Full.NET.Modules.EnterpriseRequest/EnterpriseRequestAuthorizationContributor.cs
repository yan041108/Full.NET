using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.EnterpriseRequest;

/// <summary>向授权目录贡献 Enterprise Request 样板权限、导航与页面操作。</summary>
internal sealed class EnterpriseRequestAuthorizationContributor : IAuthorizationCatalogContributor
{
    public AuthorizationModuleDefinition Module { get; } =
        new("enterprise-request", "企业申请", 81);

    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new PermissionDefinition(
            EnterpriseRequestPermissions.Read,
            "读取企业申请",
            AuthorizationScope.Host | AuthorizationScope.Tenant),
        new PermissionDefinition(
            EnterpriseRequestPermissions.Create,
            "创建企业申请",
            AuthorizationScope.Host | AuthorizationScope.Tenant),
        new PermissionDefinition(
            EnterpriseRequestPermissions.Update,
            "更新企业申请",
            AuthorizationScope.Host | AuthorizationScope.Tenant),
        new PermissionDefinition(
            EnterpriseRequestPermissions.Disable,
            "停用企业申请",
            AuthorizationScope.Host | AuthorizationScope.Tenant),
    ];

    public IReadOnlyCollection<NavigationDefinition> Navigation { get; } =
    [
        new NavigationDefinition(
            "enterprise-requests",
            null,
            "enterprise-requests",
            "/enterprise-requests",
            "enterprise-requests",
            "企业申请",
            "Enterprise Requests",
            "collection",
            80,
            EnterpriseRequestPermissions.Read),
    ];

    public IReadOnlyCollection<AuthorizationActionDefinition> Actions { get; } =
    [
        new AuthorizationActionDefinition(
            "enterprise_request.enterprise_requests.create",
            "enterprise-requests",
            EnterpriseRequestPermissions.Create,
            "创建",
            "create",
            10),
        new AuthorizationActionDefinition(
            "enterprise_request.enterprise_requests.update",
            "enterprise-requests",
            EnterpriseRequestPermissions.Update,
            "更新",
            "update",
            20),
        new AuthorizationActionDefinition(
            "enterprise_request.enterprise_requests.disable",
            "enterprise-requests",
            EnterpriseRequestPermissions.Disable,
            "停用",
            "disable",
            30),
    ];
}