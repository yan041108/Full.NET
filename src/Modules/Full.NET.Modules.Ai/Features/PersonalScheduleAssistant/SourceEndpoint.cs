using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageKnowledgeIndexing;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Calendar.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Ai.Features.PersonalScheduleAssistant;

/// <summary>本人日程页面的真实来源预览；不接受客户端正文、租户、授权或版本证明。</summary>
internal static class SourceEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapPost(
        "/api/v1/ai/personal-schedule-assistant/sources/preview", async (
            PreviewAiPersonalScheduleSources request, AiPersonalScheduleSourceReader sources, ICurrentTenant tenant,
            ICurrentSessionAuthorization authorization,
            IApiResultMapper mapper, HttpContext http, CancellationToken token) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers["X-Content-Type-Options"] = "nosniff";
            if (!AiKnowledgeIndexHttpBinding.TryCreate(http, tenant.Id, out var session))
                return mapper.Map(Failure("authorization.permission_denied", ErrorType.Forbidden), http);
            if (http.Request.Query.Count != 0 || request.ScheduleIds is not { Count: > 0 and <= 8 }
                || request.ScheduleIds.Contains(Guid.Empty) || request.ScheduleIds.Distinct().Count() != request.ScheduleIds.Count)
                return mapper.Map(Failure(PersonalScheduleKnowledgeErrorCodes.Invalid, ErrorType.Validation), http);
            string[] required = [CalendarPermissions.Read, PersonalScheduleKnowledgePermissions.Read];
            var original = await authorization.AuthorizeAllAsync(required, token).ConfigureAwait(false);
            if (original is null || original.UserId != session.UserId || original.SessionId != session.SessionId
                || original.TenantId != tenant.Id)
                return mapper.Map(Failure("authorization.permission_denied", ErrorType.Forbidden), http);
            try
            {
                var current = await sources.CaptureAsync(session.UserId, request.ScheduleIds, token).ConfigureAwait(false);
                if (await authorization.AuthorizeAllAsync(required, token).ConfigureAwait(false) != original
                    || original.TenantId != tenant.Id)
                    return mapper.Map(Failure("authorization.permission_denied", ErrorType.Forbidden), http);
                if (current is null || !await sources.RevalidateAsync(session.UserId,
                    current.Select(row => row.Proof).ToArray(), token).ConfigureAwait(false))
                    return mapper.Map(Failure(PersonalScheduleKnowledgeErrorCodes.Unavailable, ErrorType.Conflict), http);
                var response = new AiPersonalScheduleSourcePreview("restricted", Array.AsReadOnly(current.Select(row =>
                    new AiPersonalScheduleSource(row.Proof.Kind, row.Proof.Purpose, row.Proof.Id, row.Proof.Version,
                        row.Proof.ProjectionHash, row.Content, row.StartAtUtc, row.EndAtUtc, row.Status)).ToArray()));
                return mapper.Map(Result<AiPersonalScheduleSourcePreview>.Success(response), http);
            }
            catch (Exception error) when (error is InvalidDataException or IOException or TimeoutException
                || error is OperationCanceledException && !token.IsCancellationRequested)
            {
                return mapper.Map(Failure(PersonalScheduleKnowledgeErrorCodes.Unavailable, ErrorType.Conflict), http);
            }
        })
        .WithName("aiPreviewPersonalScheduleSources").WithTags("AiPersonalScheduleAssistant")
        .RequireAuthorization(FullNetPermissionPolicies.For(CalendarPermissions.Read),
            FullNetPermissionPolicies.For(PersonalScheduleKnowledgePermissions.Read))
        .Produces<AiPersonalScheduleSourcePreview>().ProducesProblem(400).ProducesProblem(401)
        .ProducesProblem(403).ProducesProblem(409);

    private static Result<AiPersonalScheduleSourcePreview> Failure(string code, ErrorType type) =>
        Result<AiPersonalScheduleSourcePreview>.Failure(new(code, "The selected personal schedule sources are unavailable.", type));
}
