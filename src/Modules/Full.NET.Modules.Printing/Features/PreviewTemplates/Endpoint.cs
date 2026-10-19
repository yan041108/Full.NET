using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Printing.Contracts;
using Full.NET.Modules.Printing.Features.PreviewTemplates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

/// <summary>Templates预览 API 路由注册。</summary>
namespace Full.NET.Modules.Printing.Features.PreviewTemplates;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/printing/templates")
            .WithTags("PrintingPreviews");

        group.MapPost("/{templateId:guid}/preview", async (
            Guid templateId,
            PreviewPrintingTemplateRequest request,
            PrintingTemplatePreviewService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service
                .PreviewAsync(templateId, request, httpContext.User, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("printingPreviewTemplate")
        .Produces<PrintingTemplatePreviewResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(PrintingTemplatePermissions.Preview));
    }
}
