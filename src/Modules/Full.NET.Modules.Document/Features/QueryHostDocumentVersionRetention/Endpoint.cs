using Full.NET.Hosting.Api;
using Full.NET.Modules.Document.Configuration;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Document.Features.QueryHostDocumentVersionRetention;

/// <summary>Host 文档版本保留策略只读查询；有效值合并 appsettings 与数据库覆盖。</summary>
internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/v1/document/host/version-retention",
                (IOptionsMonitor<DocumentVersionRetentionOptions> options) =>
                    Results.Ok(Map(options.CurrentValue)))
            .WithName("documentHostGetVersionRetentionSettings")
            .WithTags("DocumentHostSettings")
            .Produces<HostDocumentVersionRetentionSettingsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization(FullNetPermissionPolicies.For(HostDocumentPermissions.Read));
    }

    private static HostDocumentVersionRetentionSettingsResponse Map(DocumentVersionRetentionOptions options) =>
        new(
            options.MinimumRetainedVersionsPerItem,
            options.MaximumRetainedHistoryVersions,
            options.PollSeconds,
            options.BatchSize);
}
