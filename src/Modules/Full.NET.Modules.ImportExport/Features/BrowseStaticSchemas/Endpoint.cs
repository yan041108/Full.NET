using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.ImportExport.Features.BrowseStaticSchemas;

/// <summary>静态导入 Schema 目录与模板下载 HTTP 端点。</summary>
internal static class Endpoint
{
    private const string WorkbookContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/import-export/schemas")
            .WithTags("ImportExportStaticSchemas");

        group.MapGet("/", (
            StaticImportSchemaQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext) =>
        {
            var result = queries.ListAsync();
            return mapper.Map(result, httpContext);
        })
        .WithName("importExportListStaticSchemas")
        .Produces<IReadOnlyList<StaticImportSchemaDefinition>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(ImportExportPermissions.StaticSchemasRead));

        group.MapGet("/{schemaKey}", (
            string schemaKey,
            StaticImportSchemaQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext) =>
        {
            var result = queries.GetAsync(schemaKey);
            return mapper.Map(result, httpContext);
        })
        .WithName("importExportGetStaticSchema")
        .Produces<StaticImportSchemaDefinition>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(ImportExportPermissions.StaticSchemasRead));

        group.MapGet("/{schemaKey}/worksheets/{worksheetKey}/template", (
            string schemaKey,
            string worksheetKey,
            StaticImportSchemaQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext) =>
        {
            var result = queries.CreateTemplate(schemaKey, worksheetKey);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            var fileName = $"{schemaKey.Replace('.', '-')}-{worksheetKey}-template.xlsx";
            return Results.File(result.Value!, WorkbookContentType, fileName);
        })
        .WithName("importExportDownloadStaticSchemaTemplate")
        .Produces<Stream>(StatusCodes.Status200OK, WorkbookContentType)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(ImportExportPermissions.StaticSchemasRead));
    }
}
