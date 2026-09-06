using System.Security.Claims;
using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Organization.Features.ManageTenantPositions;

internal static class Endpoint
{
    private const string WorkbookContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/organization/positions")
            .WithTags("OrganizationTenantPositions");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            TenantPositionQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(
                    page ?? 1,
                    pageSize ?? 20,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("organizationListTenantPositions")
        .Produces<PagedResult<OrganizationPositionResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            OrganizationPositionManagementPermissions.Read));

        group.MapGet("/export-file", async (
            TenantPositionQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ExportAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            return Results.File(
                OrganizationPositionWorkbookCodec.Export(result.Value!),
                WorkbookContentType,
                "organization-positions.xlsx");
        })
        .WithName("organizationExportTenantPositionsWorkbook")
        .Produces<Stream>(StatusCodes.Status200OK, "application/octet-stream")
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            OrganizationPositionManagementPermissions.Export));

        group.MapGet("/import-template", () => Results.File(
                OrganizationPositionWorkbookCodec.CreateImportTemplate(),
                WorkbookContentType,
                "organization-positions-import-template.xlsx"))
            .WithName("organizationDownloadTenantPositionImportTemplate")
            .Produces<Stream>(StatusCodes.Status200OK, "application/octet-stream")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization(FullNetPermissionPolicies.For(
                OrganizationPositionManagementPermissions.Import));

        group.MapPost("/import", async (
            ImportOrganizationPositionsRequest request,
            TenantPositionManagementService service,
            ClaimsPrincipal principal,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var capabilities = ResolveImportCapabilities(principal);
            var result = await service.ImportAsync(request, capabilities, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("organizationImportTenantPositions")
        .Produces<ImportOrganizationPositionsResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            OrganizationPositionManagementPermissions.Import));

        group.MapPost("/import-file", async (
            [FromForm] IFormFile file,
            TenantPositionManagementService service,
            ClaimsPrincipal principal,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                return InvalidWorkbook(mapper, httpContext);
            }

            try
            {
                await using var stream = file.OpenReadStream();
                var rows = await OrganizationPositionWorkbookCodec.ParseImportAsync(
                        stream,
                        file.Length,
                        cancellationToken)
                    .ConfigureAwait(false);
                var capabilities = ResolveImportCapabilities(principal);
                var result = await service.ImportAsync(
                        new ImportOrganizationPositionsRequest(rows),
                        capabilities,
                        cancellationToken)
                    .ConfigureAwait(false);
                return mapper.Map(result, httpContext);
            }
            catch (InvalidDataException)
            {
                return InvalidWorkbook(mapper, httpContext);
            }
        })
        .WithName("organizationImportTenantPositionsWorkbook")
        .Accepts<IFormFile>("multipart/form-data")
        .Produces<ImportOrganizationPositionsResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .DisableAntiforgery()
        .RequireAuthorization(FullNetPermissionPolicies.For(
            OrganizationPositionManagementPermissions.Import));

        group.MapGet("/{positionId:guid}", async (
            Guid positionId,
            TenantPositionQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(positionId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("organizationGetTenantPosition")
        .Produces<OrganizationPositionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            OrganizationPositionManagementPermissions.Read));

        group.MapPost("/", async (
            CreateOrganizationPositionRequest request,
            TenantPositionManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            return Results.Created(
                $"/api/v1/organization/positions/{result.Value!.Id:D}",
                result.Value);
        })
        .WithName("organizationCreateTenantPosition")
        .Produces<OrganizationPositionResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            OrganizationPositionManagementPermissions.Create));

        group.MapPut("/{positionId:guid}", async (
            Guid positionId,
            UpdateOrganizationPositionRequest request,
            TenantPositionManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(positionId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("organizationUpdateTenantPosition")
        .Produces<OrganizationPositionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            OrganizationPositionManagementPermissions.Update));

        group.MapPut("/{positionId:guid}/unit", async (
            Guid positionId,
            AssignOrganizationPositionUnitRequest request,
            TenantPositionManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.AssignUnitAsync(
                    positionId,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("organizationAssignTenantPositionUnit")
        .Produces<OrganizationPositionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            OrganizationPositionManagementPermissions.AssignUnit));

        group.MapPut("/{positionId:guid}/position-level", async (
            Guid positionId,
            AssignOrganizationPositionLevelRequest request,
            TenantPositionManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.AssignPositionLevelAsync(
                    positionId,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("organizationAssignTenantPositionLevel")
        .Produces<OrganizationPositionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            OrganizationPositionManagementPermissions.AssignPositionLevel));

        group.MapPost("/{positionId:guid}/disable", async (
            Guid positionId,
            TenantPositionManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DisableAsync(positionId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("organizationDisableTenantPosition")
        .Produces<OrganizationPositionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            OrganizationPositionManagementPermissions.Disable));
    }

    private static OrganizationPositionImportCapabilities ResolveImportCapabilities(
        ClaimsPrincipal principal)
    {
        if (OrganizationActorContext.TryResolve(principal, out _, out var isSuperAdministrator)
            && isSuperAdministrator)
        {
            return new OrganizationPositionImportCapabilities(true, true);
        }

        return new OrganizationPositionImportCapabilities(
            HasPermission(principal, OrganizationPositionManagementPermissions.AssignUnit)
            && HasPermission(principal, OrganizationUnitManagementPermissions.Read),
            HasPermission(principal, OrganizationPositionManagementPermissions.AssignPositionLevel)
            && HasPermission(principal, OrganizationPositionLevelManagementPermissions.Read));
    }

    private static bool HasPermission(ClaimsPrincipal principal, string permissionCode) =>
        principal.FindAll(FullNetIdentityClaimTypes.Permission)
            .Any(claim => string.Equals(claim.Value, permissionCode, StringComparison.Ordinal));

    private static IResult InvalidWorkbook(
        IApiResultMapper mapper,
        HttpContext httpContext) =>
        mapper.Map(
            Result<object?>.Failure(new Error(
                OrganizationErrorCodes.PositionImportWorkbookInvalid,
                "The organization position import workbook is invalid.",
                ErrorType.Validation)),
            httpContext);
}
