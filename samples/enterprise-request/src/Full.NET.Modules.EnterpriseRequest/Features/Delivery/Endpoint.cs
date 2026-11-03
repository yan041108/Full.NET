using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Hosting.Api;
using Full.NET.Hosting.Serialization;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.EnterpriseRequest.Features.Delivery;

/// <summary>只接受资源身份和版本，主体、租户、字段策略及资金无关数据均由服务端确定。</summary>
internal static class EnterpriseRequestDeliveryEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/enterprise_request/enterprise-requests/{requestId:guid}")
            .WithTags("EnterpriseRequestDelivery")
            .RequireAuthorization(FullNetPermissionPolicies.For(EnterpriseRequestPermissions.Read));
        group.MapGet("/attachments", async (Guid requestId, int? page, int? pageSize, EnterpriseRequestDeliveryService service,
            ICurrentTenant tenant, HttpContext context, IApiResultMapper mapper, CancellationToken token) =>
        {
            if (!Binding(context, tenant, out var binding)) return Results.Unauthorized();
            return mapper.Map(await service.AttachmentsAsync(requestId, binding, page ?? 1, pageSize ?? 20, token).ConfigureAwait(false), context);
        }).WithName("enterpriseRequestListAttachments").Produces<PagedResult<EnterpriseRequestAttachmentResponse>>()
            .RequireAuthorization(FullNetPermissionPolicies.For(EnterpriseRequestDeliveryPermissions.AttachmentsRead));
        group.MapPost("/attachments", async (Guid requestId, Guid idempotencyKey, string requestVersion, IFormFile? file,
            EnterpriseRequestDeliveryService service, ICurrentTenant tenant, HttpContext context, IApiResultMapper mapper, CancellationToken token) =>
        {
            if (!Binding(context, tenant, out var binding)) return Results.Unauthorized();
            if (file is null || context.Request.Form.Files.Count != 1 || !FullNetWireScalars.TryInt64(requestVersion, out var version))
                return mapper.Map(Result<EnterpriseRequestAttachmentResponse>.Failure(new Error("enterprise_request.attachment_invalid",
                    "A single bounded file and exact version are required.", ErrorType.Validation)), context);
            await using var stream = file.OpenReadStream();
            return mapper.Map(await service.UploadAsync(requestId, idempotencyKey, version, binding, file.FileName,
                file.ContentType, stream, file.Length, token).ConfigureAwait(false), context);
        }).WithName("enterpriseRequestUploadAttachment").Accepts<IFormFile>("multipart/form-data")
            .Produces<EnterpriseRequestAttachmentResponse>()
            .WithMetadata(new RequestFormLimitsAttribute { MultipartBodyLengthLimit = EnterpriseRequestDeliveryService.MaxAttachmentBytes,
                ValueCountLimit = 4, ValueLengthLimit = 256, MultipartHeadersLengthLimit = 8192 },
                new RequestSizeLimitAttribute(12 * 1024 * 1024))
            .DisableAntiforgery()
            .RequireAuthorization(FullNetPermissionPolicies.For(EnterpriseRequestDeliveryPermissions.AttachmentsManage));
        group.MapPost("/attachments/{attachmentId:guid}/delete", async (Guid requestId, Guid attachmentId,
            DeleteEnterpriseRequestAttachmentRequest request, EnterpriseRequestDeliveryService service, ICurrentTenant tenant,
            HttpContext context, IApiResultMapper mapper, CancellationToken token) =>
        {
            if (!Binding(context, tenant, out var binding)) return Results.Unauthorized();
            return mapper.Map(await service.DeleteAttachmentAsync(requestId, attachmentId, request, binding, token).ConfigureAwait(false), context);
        }).WithName("enterpriseRequestDeleteAttachment").Produces<bool>()
            .RequireAuthorization(FullNetPermissionPolicies.For(EnterpriseRequestDeliveryPermissions.AttachmentsManage));
        group.MapGet("/attachments/{attachmentId:guid}/download", async (Guid requestId, Guid attachmentId,
            EnterpriseRequestDeliveryService service, ICurrentTenant tenant, HttpContext context, IApiResultMapper mapper, CancellationToken token) =>
        {
            if (!Binding(context, tenant, out var binding)) return Results.Unauthorized();
            var result = await service.DownloadAsync(requestId, attachmentId, "Attachment", binding, token).ConfigureAwait(false);
            return result.IsSuccess ? Results.File(result.Value!.Content, "application/octet-stream", result.Value.OriginalFileName)
                : mapper.Map(result, context);
        }).WithName("enterpriseRequestDownloadAttachment").Produces<Stream>(200, "application/octet-stream")
            .RequireAuthorization(FullNetPermissionPolicies.For(EnterpriseRequestDeliveryPermissions.AttachmentsRead));
        group.MapGet("/reports", async (Guid requestId, int? page, int? pageSize, EnterpriseRequestDeliveryService service,
            ICurrentTenant tenant, HttpContext context, IApiResultMapper mapper, CancellationToken token) =>
        {
            if (!Binding(context, tenant, out var binding)) return Results.Unauthorized();
            return mapper.Map(await service.ReportsAsync(requestId, binding, page ?? 1, pageSize ?? 20, token).ConfigureAwait(false), context);
        }).WithName("enterpriseRequestListReports").Produces<PagedResult<EnterpriseRequestReportResponse>>()
            .RequireAuthorization(FullNetPermissionPolicies.For(EnterpriseRequestDeliveryPermissions.ReportsRead));
        group.MapPost("/reports", async (Guid requestId, CreateEnterpriseRequestReportRequest request, EnterpriseRequestDeliveryService service,
            ICurrentTenant tenant, HttpContext context, IApiResultMapper mapper, CancellationToken token) =>
        {
            if (!Binding(context, tenant, out var binding)) return Results.Unauthorized();
            return mapper.Map(await service.CreateReportAsync(requestId, request, binding, token).ConfigureAwait(false), context);
        }).WithName("enterpriseRequestCreateReport").Produces<EnterpriseRequestReportResponse>()
            .RequireAuthorization(FullNetPermissionPolicies.For(EnterpriseRequestDeliveryPermissions.ReportsCreate),
                FullNetPermissionPolicies.For(EnterpriseRequestDeliveryPermissions.ReportsRead),
                FullNetPermissionPolicies.For(EnterpriseRequestDetailPermissions.Read));
        group.MapPost("/reports/{reportId:guid}/cancel", async (Guid requestId, Guid reportId, CancelEnterpriseRequestReportRequest request,
            EnterpriseRequestDeliveryService service, ICurrentTenant tenant, HttpContext context, IApiResultMapper mapper, CancellationToken token) =>
        {
            if (!Binding(context, tenant, out var binding)) return Results.Unauthorized();
            return mapper.Map(await service.CancelReportAsync(requestId, reportId, request, binding, token).ConfigureAwait(false), context);
        }).WithName("enterpriseRequestCancelReport").Produces<EnterpriseRequestReportResponse>()
            .RequireAuthorization(FullNetPermissionPolicies.For(EnterpriseRequestDeliveryPermissions.ReportsCancel),
                FullNetPermissionPolicies.For(EnterpriseRequestDeliveryPermissions.ReportsRead));
        group.MapGet("/reports/{reportId:guid}/download", async (Guid requestId, Guid reportId, EnterpriseRequestDeliveryService service,
            ICurrentTenant tenant, HttpContext context, IApiResultMapper mapper, CancellationToken token) =>
        {
            if (!Binding(context, tenant, out var binding)) return Results.Unauthorized();
            var result = await service.DownloadAsync(requestId, reportId, "Report", binding, token).ConfigureAwait(false);
            return result.IsSuccess ? Results.File(result.Value!.Content, EnterpriseRequestDeliveryProcessor.WorkbookType, result.Value.OriginalFileName)
                : mapper.Map(result, context);
        }).WithName("enterpriseRequestDownloadReport").Produces<Stream>(200, EnterpriseRequestDeliveryProcessor.WorkbookType)
            .RequireAuthorization(FullNetPermissionPolicies.For(EnterpriseRequestDeliveryPermissions.ReportsRead));
        group.MapPost("/print", async (Guid requestId, EnterpriseRequestDeliveryService service, ICurrentTenant tenant,
            HttpContext context, IApiResultMapper mapper, CancellationToken token) =>
        {
            if (!Binding(context, tenant, out var binding)) return Results.Unauthorized();
            return mapper.Map(await service.PrintAsync(requestId, binding, token).ConfigureAwait(false), context);
        }).WithName("enterpriseRequestPrintDocument").Produces<EnterpriseRequestPrintResponse>()
            .RequireAuthorization(FullNetPermissionPolicies.For(EnterpriseRequestDeliveryPermissions.Print),
                FullNetPermissionPolicies.For(EnterpriseRequestDetailPermissions.Read));
    }

    private static bool Binding(HttpContext context, ICurrentTenant tenant, out SessionBindingSnapshot binding)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        return EnterpriseRequestSessionBinding.TryRead(context.User, tenant.Id, out binding);
    }
}
