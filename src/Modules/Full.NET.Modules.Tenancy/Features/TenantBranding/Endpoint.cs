using System.Security.Claims;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.Modules.Tenancy.Features.TenantBranding;

internal static class Endpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/branding/current", async (
            TenantBrandingService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetRuntimeAsync(cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("tenancyGetRuntimeBranding")
        .Produces<TenantRuntimeBrandingResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .AllowAnonymous();

        group.MapGet("/branding", async (
            TenantBrandingService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("tenancyGetCurrentBranding")
        .Produces<TenantBrandingResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(TenantBrandingPermissions.Read));

        group.MapPut("/branding", async (
            UpdateTenantBrandingRequest request,
            TenantBrandingService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateCurrentAsync(request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("tenancyUpdateCurrentBranding")
        .Produces<TenantBrandingResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(TenantBrandingPermissions.Update));

        group.MapPost("/branding/logo", UploadCurrentLogo)
            .WithName("tenancyUploadCurrentBrandingLogo")
            .Produces<TenantBrandingResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization(FullNetPermissionPolicies.For(TenantBrandingPermissions.Update))
            .DisableAntiforgery();

        group.MapDelete("/branding/logo", DeleteCurrentLogo)
            .WithName("tenancyDeleteCurrentBrandingLogo")
            .Produces<TenantBrandingResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization(FullNetPermissionPolicies.For(TenantBrandingPermissions.Update));

        group.MapGet("/branding/logo/content", GetCurrentLogoContent)
            .WithName("tenancyGetCurrentBrandingLogoContent")
            .Produces<Stream>(StatusCodes.Status200OK, "application/octet-stream")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .AllowAnonymous();
    }

    public static void MapHostTenantBranding(RouteGroupBuilder group)
    {
        group.MapGet("/{tenantId:guid}/branding", async (
            Guid tenantId,
            TenantBrandingService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetByTenantIdAsync(tenantId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("tenancyGetHostTenantBranding")
        .Produces<TenantBrandingResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenancyTenantManagementPermissions.HostTenantsRead));

        group.MapPut("/{tenantId:guid}/branding", async (
            Guid tenantId,
            UpdateTenantBrandingRequest request,
            TenantBrandingService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateByTenantIdAsync(tenantId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("tenancyUpdateHostTenantBranding")
        .Produces<TenantBrandingResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenancyTenantManagementPermissions.Update));

        group.MapPost("/{tenantId:guid}/branding/logo", UploadHostLogo)
            .WithName("tenancyUploadHostTenantBrandingLogo")
            .Produces<TenantBrandingResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization(FullNetPermissionPolicies.For(
                TenancyTenantManagementPermissions.Update))
            .DisableAntiforgery();

        group.MapDelete("/{tenantId:guid}/branding/logo", DeleteHostLogo)
            .WithName("tenancyDeleteHostTenantBrandingLogo")
            .Produces<TenantBrandingResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization(FullNetPermissionPolicies.For(
                TenancyTenantManagementPermissions.Update));

        group.MapGet("/{tenantId:guid}/branding/logo/content", GetHostLogoContent)
            .WithName("tenancyGetHostTenantBrandingLogoContent")
            .Produces<Stream>(StatusCodes.Status200OK, "application/octet-stream")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .AllowAnonymous();
    }

    private static async Task<IResult> UploadHostLogo(
        Guid tenantId,
        IFormFile? file,
        ClaimsPrincipal principal,
        TenantBrandingMediaService mediaService,
        IApiResultMapper mapper,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        await UploadLogoAsync(
            tenantId,
            file,
            principal,
            mediaService,
            mapper,
            httpContext,
            (service, id, userId, uploadFile, stream, length, token) =>
                service.UploadLogoByTenantIdAsync(
                    id,
                    userId,
                    uploadFile.FileName,
                    uploadFile.ContentType,
                    stream,
                    length,
                    token),
            cancellationToken).ConfigureAwait(false);

    private static async Task<IResult> UploadCurrentLogo(
        IFormFile? file,
        ClaimsPrincipal principal,
        ICurrentTenant currentTenant,
        TenantBrandingMediaService mediaService,
        IApiResultMapper mapper,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!currentTenant.IsAvailable || currentTenant.Id is not Guid tenantId)
        {
            return mapper.Map(NotFound<TenantBrandingResponse>(), httpContext);
        }

        return await UploadLogoAsync(
            tenantId,
            file,
            principal,
            mediaService,
            mapper,
            httpContext,
            (service, id, userId, uploadFile, stream, length, token) =>
                service.UploadLogoCurrentAsync(
                    id,
                    userId,
                    uploadFile.FileName,
                    uploadFile.ContentType,
                    stream,
                    length,
                    token),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IResult> UploadLogoAsync(
        Guid tenantId,
        IFormFile? file,
        ClaimsPrincipal principal,
        TenantBrandingMediaService mediaService,
        IApiResultMapper mapper,
        HttpContext httpContext,
        Func<TenantBrandingMediaService, Guid, Guid, IFormFile, Stream, long, CancellationToken, Task<Result<TenantBrandingResponse>>> handler,
        CancellationToken cancellationToken)
    {
        if (!TryReadUserId(principal, out var userId))
        {
            return mapper.Map(Unauthorized<TenantBrandingResponse>(), httpContext);
        }

        if (file is null || file.Length == 0)
        {
            return mapper.Map(LogoInvalid<TenantBrandingResponse>(), httpContext);
        }

        await using var stream = file.OpenReadStream();
        var result = await handler(
                mediaService,
                tenantId,
                userId,
                file,
                stream,
                file.Length,
                cancellationToken)
            .ConfigureAwait(false);
        return mapper.Map(result, httpContext);
    }

    private static async Task<IResult> DeleteHostLogo(
        Guid tenantId,
        TenantBrandingMediaService mediaService,
        IApiResultMapper mapper,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await mediaService.DeleteLogoByTenantIdAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);
        return mapper.Map(result, httpContext);
    }

    private static async Task<IResult> DeleteCurrentLogo(
        ICurrentTenant currentTenant,
        TenantBrandingMediaService mediaService,
        IApiResultMapper mapper,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!currentTenant.IsAvailable || currentTenant.Id is not Guid tenantId)
        {
            return mapper.Map(NotFound<TenantBrandingResponse>(), httpContext);
        }

        var result = await mediaService.DeleteLogoCurrentAsync(
                tenantId,
                cancellationToken)
            .ConfigureAwait(false);
        return mapper.Map(result, httpContext);
    }

    private static async Task<IResult> GetHostLogoContent(
        Guid tenantId,
        TenantBrandingMediaService mediaService,
        IApiResultMapper mapper,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        await OpenLogoContentAsync(
            tenantId,
            mediaService,
            mapper,
            httpContext,
            cancellationToken).ConfigureAwait(false);

    private static async Task<IResult> GetCurrentLogoContent(
        ICurrentTenant currentTenant,
        TenantBrandingMediaService mediaService,
        IApiResultMapper mapper,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!currentTenant.IsAvailable || currentTenant.Id is not Guid tenantId)
        {
            return mapper.Map(LogoNotFound<HostFileContent>(), httpContext);
        }

        return await OpenLogoContentAsync(
            tenantId,
            mediaService,
            mapper,
            httpContext,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IResult> OpenLogoContentAsync(
        Guid tenantId,
        TenantBrandingMediaService mediaService,
        IApiResultMapper mapper,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await mediaService.OpenLogoContentAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return mapper.Map(result, httpContext);
        }

        var content = result.Value!;
        return Results.File(
            content.Content,
            content.ContentType,
            content.OriginalFileName,
            enableRangeProcessing: true);
    }

    private static bool TryReadUserId(ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(
            principal.FindFirstValue(JwtRegisteredClaimNames.Sub),
            out userId);

    private static Result<T> Unauthorized<T>() =>
        Result<T>.Failure(new Error(
            IdentityErrorCodes.SessionNotActive,
            "The current session is no longer active.",
            ErrorType.Unauthorized));

    private static Result<T> NotFound<T>() =>
        Result<T>.Failure(new Error(
            TenancyErrorCodes.NotFound,
            "The tenant was not found.",
            ErrorType.NotFound));

    private static Result<T> LogoInvalid<T>() =>
        Result<T>.Failure(new Error(
            TenancyErrorCodes.BrandingLogoInvalid,
            "The uploaded logo file is invalid for tenant branding.",
            ErrorType.Validation));

    private static Result<T> LogoNotFound<T>() =>
        Result<T>.Failure(new Error(
            TenancyErrorCodes.BrandingLogoNotFound,
            "The requested tenant logo is not available.",
            ErrorType.NotFound));
}
