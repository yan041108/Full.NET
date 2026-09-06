using Full.NET.Hosting.Api;
using Full.NET.Modules.Auditing.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Auditing.Features.ExportHostAuditLogs;

internal static class Endpoint
{
    private const string WorkbookContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        MapExport(
            endpoints,
            "/api/v1/auditing/access-logs/exports",
            "auditingExportHostAccessLogs",
            "AuditingHostAccessLogs",
            AccessLogExportPermissions.Export,
            (service, request, includeSensitive, cancellationToken) =>
                service.ExportAccessAsync(request, includeSensitive, cancellationToken));

        MapExport(
            endpoints,
            "/api/v1/auditing/operation-logs/exports",
            "auditingExportHostOperationLogs",
            "AuditingHostOperationLogs",
            OperationLogExportPermissions.Export,
            (service, request, includeSensitive, cancellationToken) =>
                service.ExportOperationAsync(request, includeSensitive, cancellationToken));

        MapExport(
            endpoints,
            "/api/v1/auditing/exception-logs/exports",
            "auditingExportHostExceptionLogs",
            "AuditingHostExceptionLogs",
            ExceptionLogExportPermissions.Export,
            (service, request, includeSensitive, cancellationToken) =>
                service.ExportExceptionAsync(request, includeSensitive, cancellationToken));
    }

    private static void MapExport(
        IEndpointRouteBuilder endpoints,
        string route,
        string operationName,
        string tag,
        string exportPermission,
        Func<HostAuditLogExportService, AuditLogExportRequest, bool, CancellationToken, Task<Full.NET.Abstractions.Results.Result<AuditLogExportFileResult>>> export)
    {
        endpoints.MapPost(route, async (
            AuditLogExportRequest request,
            HostAuditLogExportService exportService,
            IAuthorizationService authorizationService,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var includeSensitive = (await authorizationService.AuthorizeAsync(
                    httpContext.User,
                    null,
                    FullNetPermissionPolicies.For(AuditLogExportPermissions.SensitiveFields))
                .ConfigureAwait(false)).Succeeded;
            var result = await export(
                    exportService,
                    request,
                    includeSensitive,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            var exportResult = result.Value!;
            httpContext.Response.Headers["X-FullNet-Export-Row-Count"] =
                exportResult.Metadata.RowCount.ToString();
            httpContext.Response.Headers["X-FullNet-Export-Truncated"] =
                exportResult.Metadata.Truncated ? "true" : "false";
            httpContext.Response.Headers["X-FullNet-Export-Includes-Sensitive-Fields"] =
                exportResult.Metadata.IncludesSensitiveFields ? "true" : "false";
            return Results.File(
                exportResult.FileBytes,
                WorkbookContentType,
                exportResult.Metadata.FileName);
        })
        .WithName(operationName)
        .WithTags(tag)
        .Produces<Stream>(StatusCodes.Status200OK, WorkbookContentType)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(exportPermission));
    }
}
