using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.CodeGeneration.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.CodeGeneration.Features.BrowseHostCatalog;

/// <summary>
/// 映射 Host 只读数据库目录端点。
/// </summary>
internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/v1/code-generation/catalog")
            .WithTags("CodeGenerationCatalog");

        group.MapGet("/tables", async (
            CodeGenerationCatalogQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListTablesAsync(cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("codeGenerationListCatalogTables")
        .Produces<IReadOnlyList<CodeGenerationCatalogTableResponse>>(
            StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            CodeGenerationCatalogPermissions.Read));

        group.MapGet("/tables/{tableName}/columns", async (
            string tableName,
            CodeGenerationCatalogQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListColumnsAsync(
                    tableName,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("codeGenerationListCatalogColumns")
        .Produces<CodeGenerationCatalogColumnListResponse>(
            StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            CodeGenerationCatalogPermissions.Read));

        group.MapPost("/column-sync", async (
            CodeGenerationCatalogColumnSyncRequest request,
            CodeGenerationCatalogQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.SyncColumnsAsync(
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("codeGenerationSyncCatalogColumns")
        .Produces<CodeGenerationCatalogColumnSyncResponse>(
            StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            CodeGenerationCatalogPermissions.Read));
    }
}
