using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Printing.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Printing.Features.ManageTemplates;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/printing/templates")
            .WithTags("PrintingTemplates");

        group.MapGet("/", async (
            string? nameContains,
            PrintingTemplateQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(nameContains, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("printingListTemplates")
        .Produces<IReadOnlyList<PrintingTemplateResponse>>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(PrintingTemplatePermissions.Read));

        group.MapGet("/{templateId:guid}", async (
            Guid templateId,
            PrintingTemplateQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(templateId, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("printingGetTemplate")
        .Produces<PrintingTemplateResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(PrintingTemplatePermissions.Read));

        group.MapPost("/", async (
            CreatePrintingTemplateRequest request,
            PrintingTemplateManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAsync(request, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            return Results.Created($"/api/v1/printing/templates/{result.Value!.Id:D}", result.Value);
        })
        .WithName("printingCreateTemplate")
        .Produces<PrintingTemplateResponse>(StatusCodes.Status201Created)
        .RequireAuthorization(FullNetPermissionPolicies.For(PrintingTemplatePermissions.Create));

        group.MapPut("/{templateId:guid}", async (
            Guid templateId,
            UpdatePrintingTemplateRequest request,
            PrintingTemplateManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(templateId, request, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("printingUpdateTemplate")
        .Produces<PrintingTemplateResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(PrintingTemplatePermissions.Update));

        group.MapPost("/{templateId:guid}/publish", async (
            Guid templateId,
            PublishPrintingTemplateRequest request,
            PrintingTemplateManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await service
                .PublishAsync(templateId, userId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("printingPublishTemplate")
        .Produces<PrintingTemplateVersionResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(PrintingTemplatePermissions.Publish));

        group.MapGet("/{templateId:guid}/versions", async (
            Guid templateId,
            PrintingTemplateQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListVersionsAsync(templateId, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("printingListTemplateVersions")
        .Produces<IReadOnlyList<PrintingTemplateVersionResponse>>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(PrintingTemplatePermissions.Read));

        group.MapGet("/{templateId:guid}/versions/{versionNumber:int}", async (
            Guid templateId,
            int versionNumber,
            PrintingTemplateQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetVersionAsync(templateId, versionNumber, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("printingGetTemplateVersion")
        .Produces<PrintingTemplateVersionResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(PrintingTemplatePermissions.Read));
    }

    private static bool TryResolveUserId(HttpContext httpContext, out Guid userId)
    {
        userId = default;
        var subject = httpContext.User.FindFirst("sub")?.Value;
        return Guid.TryParse(subject, out userId);
    }
}
