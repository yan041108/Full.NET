using System.Security.Claims;
using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.Modules.Identity.Features.SelfServiceProfile;

internal static class MediaEndpoints
{
    /// <summary>映射当前用户头像与签名媒体端点。</summary>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/me/profile/avatar", UploadAvatar)
            .WithName("identityUploadSelfServiceAvatar")
            .WithTags("IdentityMe")
            .Produces<SelfServiceProfileResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization()
            .DisableAntiforgery();

        endpoints.MapDelete("/api/v1/me/profile/avatar", DeleteAvatar)
            .WithName("identityDeleteSelfServiceAvatar")
            .WithTags("IdentityMe")
            .Produces<SelfServiceProfileResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization();

        endpoints.MapGet("/api/v1/me/profile/avatar/content", GetAvatarContent)
            .WithName("identityGetSelfServiceAvatarContent")
            .WithTags("IdentityMe")
            .Produces<Stream>(StatusCodes.Status200OK, "application/octet-stream")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        endpoints.MapPost("/api/v1/me/profile/signature", UploadSignature)
            .WithName("identityUploadSelfServiceSignature")
            .WithTags("IdentityMe")
            .Produces<SelfServiceProfileResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization()
            .DisableAntiforgery();

        endpoints.MapDelete("/api/v1/me/profile/signature", DeleteSignature)
            .WithName("identityDeleteSelfServiceSignature")
            .WithTags("IdentityMe")
            .Produces<SelfServiceProfileResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization();

        endpoints.MapGet("/api/v1/me/profile/signature/content", GetSignatureContent)
            .WithName("identityGetSelfServiceSignatureContent")
            .WithTags("IdentityMe")
            .Produces<Stream>(StatusCodes.Status200OK, "application/octet-stream")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization();
    }

    private static async Task<IResult> UploadAvatar(
        IFormFile? file,
        ClaimsPrincipal principal,
        SelfServiceProfileMediaService service,
        IApiResultMapper mapper,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        await UploadAsync(
            file,
            principal,
            service,
            mapper,
            httpContext,
            (mediaService, userId, actorScope, uploadFile, stream, length, token) =>
                mediaService.UploadAvatarAsync(
                    userId,
                    actorScope,
                    uploadFile.FileName,
                    uploadFile.ContentType,
                    stream,
                    length,
                    token),
            cancellationToken).ConfigureAwait(false);

    private static async Task<IResult> UploadSignature(
        IFormFile? file,
        ClaimsPrincipal principal,
        SelfServiceProfileMediaService service,
        IApiResultMapper mapper,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        await UploadAsync(
            file,
            principal,
            service,
            mapper,
            httpContext,
            (mediaService, userId, actorScope, uploadFile, stream, length, token) =>
                mediaService.UploadSignatureAsync(
                    userId,
                    actorScope,
                    uploadFile.FileName,
                    uploadFile.ContentType,
                    stream,
                    length,
                    token),
            cancellationToken).ConfigureAwait(false);

    private static async Task<IResult> UploadAsync(
        IFormFile? file,
        ClaimsPrincipal principal,
        SelfServiceProfileMediaService service,
        IApiResultMapper mapper,
        HttpContext httpContext,
        Func<SelfServiceProfileMediaService, Guid, string, IFormFile, Stream, long, CancellationToken, Task<Result<SelfServiceProfileResponse>>> handler,
        CancellationToken cancellationToken)
    {
        if (!TryReadIdentity(principal, out var userId, out var actorScope))
        {
            return mapper.Map(Unauthorized<SelfServiceProfileResponse>(), httpContext);
        }

        if (file is null || file.Length == 0)
        {
            return mapper.Map(MediaInvalid<SelfServiceProfileResponse>(), httpContext);
        }

        await using var stream = file.OpenReadStream();
        var result = await handler(
                service,
                userId,
                actorScope,
                file,
                stream,
                file.Length,
                cancellationToken)
            .ConfigureAwait(false);
        return mapper.Map(result, httpContext);
    }

    private static async Task<IResult> DeleteAvatar(
        ClaimsPrincipal principal,
        SelfServiceProfileMediaService service,
        IApiResultMapper mapper,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!TryReadIdentity(principal, out var userId, out var actorScope))
        {
            return mapper.Map(Unauthorized<SelfServiceProfileResponse>(), httpContext);
        }

        var result = await service.DeleteAvatarAsync(userId, actorScope, cancellationToken)
            .ConfigureAwait(false);
        return mapper.Map(result, httpContext);
    }

    private static async Task<IResult> DeleteSignature(
        ClaimsPrincipal principal,
        SelfServiceProfileMediaService service,
        IApiResultMapper mapper,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!TryReadIdentity(principal, out var userId, out var actorScope))
        {
            return mapper.Map(Unauthorized<SelfServiceProfileResponse>(), httpContext);
        }

        var result = await service.DeleteSignatureAsync(userId, actorScope, cancellationToken)
            .ConfigureAwait(false);
        return mapper.Map(result, httpContext);
    }

    private static async Task<IResult> GetAvatarContent(
        ClaimsPrincipal principal,
        SelfServiceProfileMediaService service,
        IApiResultMapper mapper,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        await OpenContentAsync(
            principal,
            service,
            mapper,
            httpContext,
            (mediaService, userId, actorScope, token) =>
                mediaService.OpenAvatarContentAsync(userId, actorScope, token),
            cancellationToken).ConfigureAwait(false);

    private static async Task<IResult> GetSignatureContent(
        ClaimsPrincipal principal,
        SelfServiceProfileMediaService service,
        IApiResultMapper mapper,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        await OpenContentAsync(
            principal,
            service,
            mapper,
            httpContext,
            (mediaService, userId, actorScope, token) =>
                mediaService.OpenSignatureContentAsync(userId, actorScope, token),
            cancellationToken).ConfigureAwait(false);

    private static async Task<IResult> OpenContentAsync(
        ClaimsPrincipal principal,
        SelfServiceProfileMediaService service,
        IApiResultMapper mapper,
        HttpContext httpContext,
        Func<SelfServiceProfileMediaService, Guid, string, CancellationToken, Task<Result<HostFileContent>>> handler,
        CancellationToken cancellationToken)
    {
        if (!TryReadIdentity(principal, out var userId, out var actorScope))
        {
            return mapper.Map(Unauthorized<HostFileContent>(), httpContext);
        }

        var result = await handler(service, userId, actorScope, cancellationToken)
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

    private static bool TryReadIdentity(
        ClaimsPrincipal principal,
        out Guid userId,
        out string actorScope)
    {
        actorScope = principal.FindFirstValue(IdentityClaimTypes.ActorScope) ?? string.Empty;
        return Guid.TryParse(
                   principal.FindFirstValue(JwtRegisteredClaimNames.Sub),
                   out userId)
               && !string.IsNullOrWhiteSpace(actorScope);
    }

    private static Result<T> Unauthorized<T>() =>
        Result<T>.Failure(new Error(
            IdentityErrorCodes.SessionNotActive,
            "The current session is no longer active.",
            ErrorType.Unauthorized));

    private static Result<T> MediaInvalid<T>() =>
        Result<T>.Failure(new Error(
            IdentityErrorCodes.SelfServiceProfileMediaInvalid,
            "The uploaded media file is invalid for this profile field.",
            ErrorType.Validation));
}
