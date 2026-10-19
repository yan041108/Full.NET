using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Auditing.Contracts;

/// <summary>Auditing 模块授权目录贡献者；向 Identity 注册权限、导航与精确动作。</summary>
namespace Full.NET.Modules.Auditing;

internal sealed class AuditingAuthorizationContributor
    : IAuthorizationCatalogContributor
{
    public AuthorizationModuleDefinition Module { get; } =
        new("auditing", "审计日志", 110);

    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new PermissionDefinition(Features.QueryLogTimeline.LogTimelinePermissions.Read, "查询日志关联时间线", AuthorizationScope.Host),
        new PermissionDefinition(
            AccessLogPermissions.Read,
            "查询访问日志",
            AuthorizationScope.Host),
        new PermissionDefinition(
            OperationLogPermissions.Read,
            "查询操作日志",
            AuthorizationScope.Host),
        new PermissionDefinition(
            OperationLogPermissions.ReadDetails,
            "查询操作日志受限详情",
            AuthorizationScope.Host),
        new PermissionDefinition(
            ExceptionLogPermissions.Read,
            "查询异常日志",
            AuthorizationScope.Host),
        new PermissionDefinition(
            OutboundCallLogPermissions.Read,
            "查询出站调用审计",
            AuthorizationScope.Host),
        new PermissionDefinition(
            AuditLogTrendPermissions.Read,
            "查询审计日志趋势",
            AuthorizationScope.Host),
        new PermissionDefinition(
            DomainChangeDiffPermissions.Read,
            "查询域审计变更差异",
            AuthorizationScope.Host),
        new PermissionDefinition(
            AccessLogExportPermissions.Export,
            "导出访问日志",
            AuthorizationScope.Host),
        new PermissionDefinition(
            OperationLogExportPermissions.Export,
            "导出操作日志",
            AuthorizationScope.Host),
        new PermissionDefinition(
            ExceptionLogExportPermissions.Export,
            "导出异常日志",
            AuthorizationScope.Host),
        new PermissionDefinition(
            AuditLogExportPermissions.SensitiveFields,
            "导出审计日志敏感字段",
            AuthorizationScope.Host),
    ];

    public IReadOnlyCollection<NavigationDefinition> Navigation { get; } =
    [
        new NavigationDefinition("log-timeline", null, "log-timeline", "/auditing/log-timeline", "log-timeline",
            "日志时间线", "Log Timeline", "document", 58, Features.QueryLogTimeline.LogTimelinePermissions.Read),
        new NavigationDefinition(
            "authentication-events",
            null,
            "authentication-events",
            "/auditing/authentication-events",
            "authentication-events",
            "认证事件日志",
            "Authentication Events",
            "document",
            59,
            AuthenticationEventPermissions.Read),
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
