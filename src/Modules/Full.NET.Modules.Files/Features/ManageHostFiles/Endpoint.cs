using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Storage;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;



namespace Full.NET.Modules.Files.Features.ManageHostFiles;



internal static class Endpoint

{

    public static void Map(IEndpointRouteBuilder endpoints)

    {

        var group = endpoints.MapGroup("/api/v1/files/host-files")

            .WithTags("Files");



        group.MapGet("/", async (

            int? page,

            int? pageSize,

            HostFileQueryService queries,

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

        .Produces<PagedResult<HostFileResponse>>(StatusCodes.Status200OK)

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

        .Produces<HostFileResponse>(StatusCodes.Status200OK)

        .RequireAuthorization(FullNetPermissionPolicies.For(HostFilePermissions.Read));



        group.MapPost("/", async (

            IFormFile? file,

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

        .Produces<HostFileResponse>(StatusCodes.Status201Created)

        .DisableAntiforgery()

        .RequireAuthorization(FullNetPermissionPolicies.For(HostFilePermissions.Write));



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

            // 下载必须服从对象创建时持久化的 Provider，切换默认配置不能改变既有对象归属。
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

        .Produces(StatusCodes.Status200OK)

        .RequireAuthorization(FullNetPermissionPolicies.For(HostFilePermissions.Read));



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

        .Produces<HostFileResponse>(StatusCodes.Status200OK)

        .RequireAuthorization(FullNetPermissionPolicies.For(HostFilePermissions.Write));

    }



    private static bool TryResolveUserId(HttpContext httpContext, out Guid userId)

    {

        userId = default;

        var subject = httpContext.User.FindFirst("sub")?.Value;

        return Guid.TryParse(subject, out userId);

    }

}

