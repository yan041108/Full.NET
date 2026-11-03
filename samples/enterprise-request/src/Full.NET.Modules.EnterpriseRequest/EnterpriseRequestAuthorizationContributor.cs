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
        new(EnterpriseRequestDetailPermissions.Read, "读取企业申请明细", AuthorizationScope.Tenant),
        new(Contracts.EnterpriseRequestDeliveryPermissions.AttachmentsRead, "读取企业申请附件", AuthorizationScope.Tenant),
        new(Contracts.EnterpriseRequestDeliveryPermissions.AttachmentsManage, "管理企业申请附件", AuthorizationScope.Tenant),
        new(Contracts.EnterpriseRequestDeliveryPermissions.ReportsCreate, "创建企业申请报表", AuthorizationScope.Tenant),
        new(Contracts.EnterpriseRequestDeliveryPermissions.ReportsRead, "读取企业申请报表", AuthorizationScope.Tenant),
        new(Contracts.EnterpriseRequestDeliveryPermissions.ReportsCancel, "取消企业申请报表", AuthorizationScope.Tenant),
        new(Contracts.EnterpriseRequestDeliveryPermissions.Print, "打印企业申请", AuthorizationScope.Tenant),
        new(Contracts.EnterpriseRequestDeliveryPermissions.AmountsRead, "读取企业申请交付金额", AuthorizationScope.Tenant),
        new PermissionDefinition(Contracts.EnterpriseRequestWorkflowConstants.SubmitPermission,
            "提交企业申请审批", AuthorizationScope.Tenant),
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
