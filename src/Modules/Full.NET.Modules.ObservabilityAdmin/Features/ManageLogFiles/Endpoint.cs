using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.ObservabilityAdmin.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.ObservabilityAdmin.Features.ManageLogFiles;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/observability/log-files")
            .WithTags("ObservabilityLogFiles");

        group.MapGet("/", (LogFileControlPlane controlPlane) =>
                Results.Ok(controlPlane.List()))
            .WithName("observabilityListLogFiles")
            .Produces<IReadOnlyList<LogFileSummary>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization(FullNetPermissionPolicies.For(
                ObservabilityLogFilePermissions.Read));

        group.MapGet("/{id}/tail", async (
            string id,
            int? maximumLines,
            int? maximumBytes,
            LogFileControlPlane controlPlane,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await controlPlane.ReadTailAsync(
                    id,
                    maximumLines,
                    maximumBytes,
                    cancellationToken)
                .ConfigureAwait(false);
            return result is null
                ? NotFound(mapper, httpContext)
                : Results.Ok(result);
        })
        .WithName("observabilityTailLogFile")
        .Produces<LogFileTail>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            ObservabilityLogFilePermissions.Read));

        group.MapGet("/{id}/download", (
            string id,
            LogFileControlPlane controlPlane,
            IApiResultMapper mapper,
            HttpContext httpContext) =>
        {
            var result = controlPlane.OpenDownload(id);
            return result is null
                ? NotFound(mapper, httpContext)
                : Results.File(
                    result.Content,
                    "text/plain; charset=utf-8",
                    result.FileName,
                    result.LastModifiedUtc,
                    enableRangeProcessing: true);
        })
        .WithName("observabilityDownloadLogFile")
        .Produces<Stream>(StatusCodes.Status200OK, "application/octet-stream")
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            ObservabilityLogFilePermissions.Download));
    }

    private static IResult NotFound(
        IApiResultMapper mapper,
        HttpContext httpContext) =>
        mapper.Map(
            Result<object?>.Failure(new Error(
                ObservabilityAdminErrorCodes.LogFileNotFound,
                "The log file was not found or has been rotated.",
                ErrorType.NotFound)),
            httpContext);
}
