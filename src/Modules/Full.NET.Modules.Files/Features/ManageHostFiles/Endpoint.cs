using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Storage;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.Modules.Files.Features.ManageHostFiles;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/files/host-files")
            .WithTags("FilesHostFiles");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            string? folderId,
            string? fileNameContains,
            HostFileQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var filter = HostFileQueryService
                .ParseFolderFilter(folderId)
                .WithFileNameContains(fileNameContains);
            var result = await queries.ListAsync(
                    page ?? 1,
                    pageSize ?? 20,
                    filter,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("filesListHostFiles")
        .Produces<PagedResult<HostFileResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostFilePermissions.Read));

        group.MapGet("/{fileId:guid}", async (
            Guid fileId,
            HostFileQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(fileId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("filesGetHostFile")
        .Produces<HostFileResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostFilePermissions.Read));

        group.MapPost("/", async (
            IFormFile? file,
            Guid? folderId,
            HostFileManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            if (file is null)
            {
                return mapper.Map(
                    Result<HostFileResponse>.Failure(new Error(
                        FilesErrorCodes.InvalidUpload,
                        "Multipart file field is required.",
                        ErrorType.Validation)),
                    httpContext);
            }

            await using var stream = file.OpenReadStream();
            var result = await service.UploadAsync(
                    userId,
                    file.FileName,
                    file.ContentType,
                    stream,
                    file.Length,
                    folderId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            return Results.Created(
                $"/api/v1/files/host-files/{result.Value!.Id:D}",
                result.Value);
        })
        .WithName("filesUploadHostFile")
        .Accepts<IFormFile>("multipart/form-data")
        .Produces<HostFileResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .DisableAntiforgery()
        .RequireAuthorization(FullNetPermissionPolicies.For(HostFilePermissions.Upload));

        group.MapPost("/{fileId:guid}/update", async (
            Guid fileId,
            UpdateHostFileMetadataRequest request,
            HostFileManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetSubject(httpContext.User, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.UpdateMetadataAsync(
                    fileId,
                    userId,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("filesUpdateHostFileMetadata")
        .Produces<HostFileResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostFilePermissions.Update));

        group.MapGet("/{fileId:guid}/references", async (
            Guid fileId,
            int? page,
            int? pageSize,
            HostFileQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListReferencesAsync(
                    fileId,
                    page ?? 1,
                    pageSize ?? 20,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("filesListHostFileReferences")
        .Produces<PagedResult<HostFileReferenceClaimResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostFilePermissions.ReferencesRead));

        group.MapGet("/{fileId:guid}/content", async (
            Guid fileId,
            HostFileQueryService queries,
            FileStorageProviderRegistry storageProviders,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var detailResult = await queries.GetDetailByIdAsync(fileId, cancellationToken)
                .ConfigureAwait(false);
            if (!detailResult.IsSuccess)
            {
                return mapper.Map(
                    Result<HostFileResponse>.Failure(detailResult.Error!),
                    httpContext);
            }

            var detail = detailResult.Value!;
            var storageProvider = storageProviders.Resolve(detail.ProviderKey);
            var stream = await storageProvider.OpenReadAsync(
                    detail.StorageKey,
                    cancellationToken)
                .ConfigureAwait(false);

            return Results.File(
                stream,
                detail.ContentType,
                detail.OriginalFileName,
                enableRangeProcessing: true);
        })
        .WithName("filesDownloadHostFileContent")
        .Produces<Stream>(StatusCodes.Status200OK, "application/octet-stream")
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostFilePermissions.Download));

        group.MapPost("/{fileId:guid}/delete", async (
            Guid fileId,
            HostFileManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DeleteAsync(fileId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("filesDeleteHostFile")
        .Produces<HostFileResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(HostFilePermissions.Delete));
    }

    private static bool TryResolveUserId(HttpContext httpContext, out Guid userId) =>
        TryGetSubject(httpContext.User, out userId);

    private static bool TryGetSubject(
        System.Security.Claims.ClaimsPrincipal principal,
        out Guid userId)
    {
        userId = Guid.Empty;
        var subjects = principal.FindAll(JwtRegisteredClaimNames.Sub).ToArray();
        return subjects.Length == 1
            && Guid.TryParse(subjects[0].Value, out userId)
            && userId != Guid.Empty;
    }
}
