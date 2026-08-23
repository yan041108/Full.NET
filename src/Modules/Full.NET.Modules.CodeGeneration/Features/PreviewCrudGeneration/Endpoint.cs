using Full.NET.Hosting.Api;
using Full.NET.Modules.CodeGeneration.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.CodeGeneration.Features.PreviewCrudGeneration;

/// <summary>
/// 映射只读 CRUD 生成预览端点。
/// </summary>
internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
            "/api/v1/code-generation/previews",
            (
                CodeGenerationPreviewRequest request,
                CodeGenerationPreviewService service,
                IApiResultMapper mapper,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                var result = service.Preview(request, cancellationToken);
                return mapper.Map(result, httpContext);
            })
            .WithTags("CodeGenerationPreviews")
            .WithName("codeGenerationPreviewCrud")
            .Produces<CodeGenerationPreviewResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization(FullNetPermissionPolicies.For(
                CodeGenerationPreviewPermissions.Read));
    }
}
