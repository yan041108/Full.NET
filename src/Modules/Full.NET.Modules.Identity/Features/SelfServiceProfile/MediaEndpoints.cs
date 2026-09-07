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

/// <summary>当前用户头像与签名的自助媒体端点。</summary>
internal static class MediaEndpoints
{
    /// <summary>映射当前用户头像与签名媒体端点。</summary>
    /// <param name="endpoints">路由构建器。</param>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/me/profile/avatar", UploadAvatarAsync)
            .WithName("identityUploadSelfServiceAvatar")
            .WithTags("IdentityMe")
            .Produces<SelfServiceProfileResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization()
            .DisableAntiforgery();

        endpoints.MapDelete("/api/v1/me/profile/avatar", DeleteAvatarAsync)
            .WithName("identityDeleteSelfServiceAvatar")
            .WithTags("IdentityMe")
            .Produces<SelfServiceProfileResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization();

        endpoints.MapGet("/api/v1/me/profile/avatar/content", GetAvatarContentAsync)
            .WithName("identityGetSelfServiceAvatarContent")
            .WithTags("IdentityMe")
            .Produces<Stream>(StatusCodes.Status200OK, "application/octet-stream")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        endpoints.MapPost("/api/v1/me/profile/signature", UploadSignatureAsync)
            .WithName("identityUploadSelfServiceSignature")
            .WithTags("IdentityMe")
            .Produces<SelfServiceProfileResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization()
            .DisableAntiforgery();

        endpoints.MapDelete("/api/v1/me/profile/signature", DeleteSignatureAsync)
            .WithName("identityDeleteSelfServiceSignature")
            .WithTags("IdentityMe")
            .Produces<SelfServiceProfileResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization();

        endpoints.MapGet("/api/v1/me/profile/signature/content", GetSignatureContentAsync)
            .WithName("identityGetSelfServiceSignatureContent")
            .WithTags("IdentityMe")
            .Produces<Stream>(StatusCodes.Status200OK, "application/octet-stream")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization();
    }

    /// <summary>上传当前用户头像并返回更新后的资料。</summary>
    /// <param name="file">上传文件。</param>
    /// <param name="principal">当前主体。</param>
    /// <param name="service">媒体服务。</param>
    /// <param name="mapper">API 结果映射器。</param>
    /// <param name="httpContext">当前 HTTP 上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private static async Task<IResult> UploadAvatarAsync(
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

    /// <summary>上传当前用户签名并返回更新后的资料。</summary>
    /// <param name="file">上传文件。</param>
    /// <param name="principal">当前主体。</param>
    /// <param name="service">媒体服务。</param>
    /// <param name="mapper">API 结果映射器。</param>
    /// <param name="httpContext">当前 HTTP 上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private static async Task<IResult> UploadSignatureAsync(
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

    /// <summary>删除当前用户头像。</summary>
    /// <param name="principal">当前主体。</param>
    /// <param name="service">媒体服务。</param>
    /// <param name="mapper">API 结果映射器。</param>
    /// <param name="httpContext">当前 HTTP 上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private static async Task<IResult> DeleteAvatarAsync(
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

    /// <summary>删除当前用户签名。</summary>
    /// <param name="principal">当前主体。</param>
    /// <param name="service">媒体服务。</param>
    /// <param name="mapper">API 结果映射器。</param>
    /// <param name="httpContext">当前 HTTP 上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private static async Task<IResult> DeleteSignatureAsync(
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

    /// <summary>读取当前用户头像内容。</summary>
    /// <param name="principal">当前主体。</param>
    /// <param name="service">媒体服务。</param>
    /// <param name="mapper">API 结果映射器。</param>
    /// <param name="httpContext">当前 HTTP 上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private static async Task<IResult> GetAvatarContentAsync(
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

    /// <summary>读取当前用户签名内容。</summary>
    /// <param name="principal">当前主体。</param>
    /// <param name="service">媒体服务。</param>
    /// <param name="mapper">API 结果映射器。</param>
    /// <param name="httpContext">当前 HTTP 上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private static async Task<IResult> GetSignatureContentAsync(
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
