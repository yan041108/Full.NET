using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Hosting.Api;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.EnterpriseRequest.Features.ManageAttachments;

/// <summary>附件所有操作沿申请权限执行；内容强制下载，不信任上传者声明的可执行 MIME。</summary>
internal static class EnterpriseRequestAttachmentsEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/enterprise_request/enterprise-requests/{id:guid}/attachments")
            .WithTags("EnterpriseRequestEnterpriseRequests");
        group.MapGet("", async (Guid id, ClaimsPrincipal principal, EnterpriseRequestAttachmentService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken ct) => {
                if (!Actor(principal, out var actor)) return Results.Unauthorized();
                return mapper.Map(await service.ListAsync(id, actor, Super(principal), ct).ConfigureAwait(false), context);
            }).WithName("enterpriseRequestListAttachments")
            .RequireAuthorization(FullNetPermissionPolicies.For(EnterpriseRequestPermissions.Read))
            .Produces<EnterpriseRequestAttachmentsResponse>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        group.MapPost("", async (Guid id, [FromForm] long version, [FromForm] IFormFile file,
            ClaimsPrincipal principal, EnterpriseRequestAttachmentService service, IApiResultMapper mapper,
            HttpContext context, CancellationToken ct) => {
                if (!Actor(principal, out var actor)) return Results.Unauthorized();
                await using var content = file.OpenReadStream();
                return mapper.Map(await service.UploadAsync(id, version, actor, file.FileName, content, file.Length, ct).ConfigureAwait(false), context);
            }).WithName("enterpriseRequestUploadAttachment").Accepts<IFormFile>("multipart/form-data").DisableAntiforgery()
            .RequireAuthorization(FullNetPermissionPolicies.For(EnterpriseRequestPermissions.Update))
            .Produces<EnterpriseRequestAttachmentMutationResponse>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403)
            .ProducesProblem(404).ProducesProblem(409).ProducesProblem(413);
        group.MapDelete("/{attachmentId:guid}", async (Guid id, Guid attachmentId, [FromBody] RemoveEnterpriseRequestAttachmentRequest body,
            ClaimsPrincipal principal, EnterpriseRequestAttachmentService service, IApiResultMapper mapper,
            HttpContext context, CancellationToken ct) => {
                if (!Actor(principal, out var actor)) return Results.Unauthorized();
                return mapper.Map(await service.RemoveAsync(id, attachmentId, body.Version, actor, ct).ConfigureAwait(false), context);
            }).WithName("enterpriseRequestRemoveAttachment")
            .RequireAuthorization(FullNetPermissionPolicies.For(EnterpriseRequestPermissions.Update))
            .Produces<EnterpriseRequestAttachmentRemovedResponse>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        group.MapGet("/{attachmentId:guid}/content", async (Guid id, Guid attachmentId, ClaimsPrincipal principal,
            EnterpriseRequestAttachmentService service, IApiResultMapper mapper, HttpContext context, CancellationToken ct) => {
                if (!Actor(principal, out var actor)) return Results.Unauthorized();
                var result = await service.OpenAsync(id, attachmentId, actor, Super(principal), ct).ConfigureAwait(false);
                if (!result.IsSuccess) return mapper.Map(result, context);
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                context.Response.Headers.CacheControl = "no-store";
                return Results.File(result.Value!.Content, "application/octet-stream", result.Value.OriginalFileName);
            }).WithName("enterpriseRequestDownloadAttachment")
            .RequireAuthorization(FullNetPermissionPolicies.For(EnterpriseRequestPermissions.Read))
            .Produces<Stream>(StatusCodes.Status200OK, "application/octet-stream")
            .ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
    }
    private static bool Actor(ClaimsPrincipal principal, out Guid actor) =>
        Guid.TryParse(principal.FindFirstValue(FullNetIdentityClaimTypes.Subject), out actor) && actor != Guid.Empty;
    private static bool Super(ClaimsPrincipal principal) =>
        bool.TryParse(principal.FindFirstValue(FullNetIdentityClaimTypes.SuperAdministrator), out var value) && value;
}

/// <summary>附件公开 JSON 的固定 Native AOT 闭包。</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(EnterpriseRequestAttachmentsResponse))]
[JsonSerializable(typeof(EnterpriseRequestAttachmentMutationResponse))]
[JsonSerializable(typeof(RemoveEnterpriseRequestAttachmentRequest))]
[JsonSerializable(typeof(EnterpriseRequestAttachmentRemovedResponse))]
internal partial class EnterpriseRequestAttachmentsJsonContext : JsonSerializerContext;
