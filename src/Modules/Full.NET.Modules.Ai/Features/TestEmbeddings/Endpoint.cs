using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Ai.Features.TestEmbeddings;

/// <summary>Embedding 能力测试 HTTP 端点。</summary>
internal static class Endpoint
{
    /// <summary>注册 Embedding 测试路由。</summary>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/ai/model-configs/{modelConfigId:guid}/test-embeddings", async (
            Guid modelConfigId,
            TestAiModelEmbeddingRequest request,
            AiEmbeddingTestService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.TestAsync(modelConfigId, request, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("aiTestModelEmbeddings")
        .WithTags("AiModelConfigs")
        .Produces<TestAiModelEmbeddingResult>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(AiModelPermissions.Test));
    }
}
