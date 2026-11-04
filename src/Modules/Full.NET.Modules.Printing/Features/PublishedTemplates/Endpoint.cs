using Full.NET.Hosting.Api;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Printing.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Printing.Features.PublishedTemplates;

/// <summary>租户发布目录与 Host 精确版本授权端点。</summary>
internal static class Endpoint
{
    /// <summary>注册独立租户权限入口和 Host 精确版本授权入口，不放宽既有草稿权限。</summary>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/printing/published-templates", async (
            PrintingPublishedTemplateService resolver, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            mapper.Map(await resolver.ListAsync(token).ConfigureAwait(false), context))
            .WithTags("PrintingTemplates").WithName("printingListPublishedTemplates")
            .Produces<IReadOnlyList<PrintingPublishedTemplateResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization(FullNetPermissionPolicies.For(PrintingPublishedTemplatePermissions.Read));

        endpoints.MapPost("/api/v1/printing/published-templates/{templateId:guid}/preview", async (
            Guid templateId, PreviewPrintingTemplateRequest request, PrintingPublishedTemplateService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            mapper.Map(await service.PreviewAsync(templateId, request, context.User, token).ConfigureAwait(false), context))
            .WithTags("PrintingPreviews").WithName("printingPreviewPublishedTemplate")
            .Produces<PrintingTemplatePreviewResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization(FullNetPermissionPolicies.For(PrintingPublishedTemplatePermissions.Preview));

        var route = "/api/v1/printing/templates/{templateId:guid}/versions/{versionNumber:int}/tenant-grants/{tenantId:guid}";
        endpoints.MapGet("/api/v1/printing/templates/{templateId:guid}/versions/{versionNumber:int}/tenant-grants",
            async (Guid templateId, int versionNumber, int? page, int? pageSize,
                PrintingTenantGrantManagementService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
                mapper.Map(await service.ListAsync(templateId, versionNumber, page ?? 1, pageSize ?? 20, token).ConfigureAwait(false), context))
            .WithTags("PrintingTemplates").WithName("printingListTenantVersionGrants")
            .Produces<PagedResult<Guid>>()
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization(FullNetPermissionPolicies.For(PrintingTemplatePermissions.GrantTenants));
        endpoints.MapPut(route, (Guid templateId, int versionNumber, Guid tenantId,
            PrintingTenantGrantManagementService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            SetAsync(templateId, versionNumber, tenantId, true, service, mapper, context, token))
            .WithTags("PrintingTemplates").WithName("printingGrantTenantVersion").Produces<bool>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization(FullNetPermissionPolicies.For(PrintingTemplatePermissions.GrantTenants));
        endpoints.MapDelete(route, (Guid templateId, int versionNumber, Guid tenantId,
            PrintingTenantGrantManagementService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            SetAsync(templateId, versionNumber, tenantId, false, service, mapper, context, token))
            .WithTags("PrintingTemplates").WithName("printingRevokeTenantVersion").Produces<bool>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization(FullNetPermissionPolicies.For(PrintingTemplatePermissions.GrantTenants));
    }

    private static async Task<IResult> SetAsync(Guid templateId, int version, Guid tenantId, bool grant,
        PrintingTenantGrantManagementService service, IApiResultMapper mapper, HttpContext context, CancellationToken token)
    {
        if (!Guid.TryParse(context.User.FindFirst(FullNetIdentityClaimTypes.Subject)?.Value, out var actor))
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
        return mapper.Map(await service.SetAsync(templateId, version, tenantId, actor, grant, token).ConfigureAwait(false), context);
    }
}
