using Full.NET.Hosting.Api;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.ImportExport.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.ImportExport.Features.ManageImportProfiles;

/// <summary>路由仅指定静态来源与配置标识，所有者和租户始终由可信会话确定。</summary>
internal static class Endpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/me/import-profiles/{schemaKey}/{worksheetKey}").WithTags("ImportProfiles");
        group.MapGet("/", async (string schemaKey, string worksheetKey, ImportProfileService service, IApiResultMapper mapper, HttpContext http, CancellationToken token) =>
            mapper.Map(await service.ListAsync(schemaKey, worksheetKey, token).ConfigureAwait(false), http))
            .WithName("importExportListMyImportProfiles").Produces<IReadOnlyList<ImportProfileSummary>>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404)
            .RequireAuthorization(FullNetPermissionPolicies.For(ImportProfilePermissions.Read));
        group.MapGet("/{profileId:guid}", async (string schemaKey, string worksheetKey, Guid profileId, ImportProfileService service,
            IApiResultMapper mapper, HttpContext http, CancellationToken token) => mapper.Map(await service.GetAsync(schemaKey, worksheetKey, profileId, token).ConfigureAwait(false), http))
            .WithName("importExportGetMyImportProfile").Produces<ImportProfileDetail>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404)
            .RequireAuthorization(FullNetPermissionPolicies.For(ImportProfilePermissions.Read));
        group.MapPost("/", async (string schemaKey, string worksheetKey, SaveImportProfileRequest request,
            ImportProfileService service, IApiResultMapper mapper, HttpContext http, CancellationToken token) =>
        {
            var result = await service.SaveAsync(schemaKey, worksheetKey, null, request, token).ConfigureAwait(false);
            return result.IsSuccess ? Results.Created($"/api/v1/me/import-profiles/{Uri.EscapeDataString(schemaKey)}/{Uri.EscapeDataString(worksheetKey)}/{result.Value!.Summary.Id:D}", result.Value) : mapper.Map(result, http);
        }).WithName("importExportCreateMyImportProfile").Produces<ImportProfileDetail>(201).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .WithMetadata(new RequestSizeLimitAttribute(20 * 1024)).RequireAuthorization(FullNetPermissionPolicies.For(ImportProfilePermissions.Create));
        group.MapPut("/{profileId:guid}", async (string schemaKey, string worksheetKey, Guid profileId, SaveImportProfileRequest request,
            ImportProfileService service, IApiResultMapper mapper, HttpContext http, CancellationToken token) => mapper.Map(await service.SaveAsync(schemaKey, worksheetKey, profileId, request, token).ConfigureAwait(false), http))
            .WithName("importExportUpdateMyImportProfile").Produces<ImportProfileDetail>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .WithMetadata(new RequestSizeLimitAttribute(20 * 1024)).RequireAuthorization(FullNetPermissionPolicies.For(ImportProfilePermissions.Update));
        group.MapDelete("/{profileId:guid}", async (string schemaKey, string worksheetKey, Guid profileId, [FromBody] DeleteImportProfileRequest request,
            ImportProfileService service, IApiResultMapper mapper, HttpContext http, CancellationToken token) => mapper.Map(await service.DeleteAsync(schemaKey, worksheetKey, profileId, request.Version, token).ConfigureAwait(false), http))
            .WithName("importExportDeleteMyImportProfile").Produces<bool>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .RequireAuthorization(FullNetPermissionPolicies.For(ImportProfilePermissions.Delete));
    }
}
