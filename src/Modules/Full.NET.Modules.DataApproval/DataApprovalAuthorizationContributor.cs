using Full.NET.Modules.DataApproval.Contracts;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.DataApproval;

/// <summary>向授权目录贡献 DataApproval 模块权限、导航与页面操作。</summary>
internal sealed class DataApprovalAuthorizationContributor : IAuthorizationCatalogContributor
{
    /// <inheritdoc />
    public AuthorizationModuleDefinition Module { get; } =
        new("data-approval", "数据审批", 95);

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new PermissionDefinition(
            DataApprovalPermissions.Read,
            "查询数据审批请求",
            AuthorizationScope.Host),
        new PermissionDefinition(
            DataApprovalPermissions.Create,
            "创建数据审批请求",
            AuthorizationScope.Host),
        new PermissionDefinition(
            DataApprovalPermissions.Cancel,
            "取消数据审批请求",
            AuthorizationScope.Host),
    ];

    /// <inheritdoc />
    public IReadOnlyCollection<NavigationDefinition> Navigation { get; } =
    [
        new NavigationDefinition(
            "data-approval-requests",
            null,
            "data-approval-requests",
            "/data-approvals/requests",
            "data-approval-requests",
            "数据审批",
            "Data Approvals",
            "audit",
            96,
            DataApprovalPermissions.Read),
    ];

    /// <inheritdoc />
    public IReadOnlyCollection<AuthorizationActionDefinition> Actions { get; } =
    [
        new AuthorizationActionDefinition(
            "data_approvals.requests.create",
            "data-approval-requests",
            DataApprovalPermissions.Create,
            "提交审批",
            "create",
            10),
        new AuthorizationActionDefinition(
            "data_approvals.requests.cancel",
            "data-approval-requests",
            DataApprovalPermissions.Cancel,
            "取消审批",
            "cancel",
            20),
    ];
}
