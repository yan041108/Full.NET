using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Auditing.Contracts;

namespace Full.NET.Modules.Auditing;

internal sealed class AuditingAuthorizationContributor
    : IAuthorizationCatalogContributor
{
    public AuthorizationModuleDefinition Module { get; } =
        new("auditing", "审计日志", 110);

    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new PermissionDefinition(
            AccessLogPermissions.Read,
            "查询访问日志",
            AuthorizationScope.Host),
        new PermissionDefinition(
            OperationLogPermissions.Read,
            "查询操作日志",
            AuthorizationScope.Host),
        new PermissionDefinition(
            ExceptionLogPermissions.Read,
            "查询异常日志",
            AuthorizationScope.Host),
        new PermissionDefinition(
            OutboundCallLogPermissions.Read,
            "查询出站调用审计",
            AuthorizationScope.Host),
    ];

    public IReadOnlyCollection<NavigationDefinition> Navigation { get; } =
    [
        new NavigationDefinition(
            "access-logs",
            null,
            "access-logs",
            "/auditing/access-logs",
            "access-logs",
            "访问日志",
            "Access Logs",
            "document",
            60,
            AccessLogPermissions.Read),
        new NavigationDefinition(
            "operation-logs",
            null,
            "operation-logs",
            "/auditing/operation-logs",
            "operation-logs",
            "操作日志",
            "Operation Logs",
            "edit",
            61,
            OperationLogPermissions.Read),
        new NavigationDefinition(
            "exception-logs",
            null,
            "exception-logs",
            "/auditing/exception-logs",
            "exception-logs",
            "异常日志",
            "Exception Logs",
            "warning",
            62,
            ExceptionLogPermissions.Read),
        new NavigationDefinition(
            "outbound-call-logs",
            null,
            "outbound-call-logs",
            "/auditing/outbound-call-logs",
            "outbound-call-logs",
            "出站调用审计",
            "Outbound Call Logs",
            "connection",
            63,
            OutboundCallLogPermissions.Read),
    ];
}
