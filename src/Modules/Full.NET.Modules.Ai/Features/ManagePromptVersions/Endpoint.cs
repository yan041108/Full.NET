using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Ai.Features.ManagePromptVersions;

/// <summary>交互管理端显式管理草稿和发布；模板不注册为 Agent 工具。</summary>
internal static class Endpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        const string root = "/api/v1/ai/prompts";
        endpoints.MapGet(root, async (int? page, int? pageSize, AiPromptManagementService service, IApiResultMapper mapper, HttpContext http, CancellationToken token) =>
            Actor(http) is { } actor ? mapper.Map(await service.ListAsync(actor, page ?? 1, pageSize ?? 20, token).ConfigureAwait(false), http) : Results.Unauthorized())
            .WithTags("AiPrompts").WithName("aiListPrompts").Produces<PagedResult<AiPromptResponse>>(200)
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404)
            .RequireAuthorization(FullNetPermissionPolicies.For(AiPromptPermissions.Read));
        endpoints.MapGet(root + "/{promptId:guid}/versions", async (Guid promptId, int? page, int? pageSize, AiPromptManagementService service, IApiResultMapper mapper, HttpContext http, CancellationToken token) =>
            Actor(http) is { } actor ? mapper.Map(await service.VersionsAsync(actor, promptId, page ?? 1, pageSize ?? 20, token).ConfigureAwait(false), http) : Results.Unauthorized())
            .WithTags("AiPrompts").WithName("aiGetPromptVersions").Produces<PagedResult<AiPromptVersionResponse>>(200)
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404)
            .RequireAuthorization(FullNetPermissionPolicies.For(AiPromptPermissions.Read));
        endpoints.MapPost(root, async (CreateAiPromptRequest request, AiPromptManagementService service, IApiResultMapper mapper, HttpContext http, CancellationToken token) =>
        {
            if (Actor(http) is not { } actor) return Results.Unauthorized();
            var result = await service.CreateAsync(actor, request, token).ConfigureAwait(false);
            return result.IsSuccess ? Results.Created(root + "/" + result.Value!.Id, result.Value) : mapper.Map(result, http);
        }).WithTags("AiPrompts").WithName("aiCreatePrompt").Produces<AiPromptResponse>(201)
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .RequireAuthorization(FullNetPermissionPolicies.For(AiPromptPermissions.Create));
        endpoints.MapPost(root + "/{promptId:guid}/versions", async (Guid promptId, AppendAiPromptVersionRequest request, AiPromptManagementService service, IApiResultMapper mapper, HttpContext http, CancellationToken token) =>
            Actor(http) is { } actor ? mapper.Map(await service.AppendAsync(actor, promptId, request, token).ConfigureAwait(false), http) : Results.Unauthorized())
            .WithTags("AiPrompts").WithName("aiAppendPromptVersion").Produces<AiPromptResponse>(200)
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .RequireAuthorization(FullNetPermissionPolicies.For(AiPromptPermissions.Append));
        endpoints.MapPost(root + "/{promptId:guid}/publish", async (Guid promptId, SelectAiPromptVersionRequest request, AiPromptManagementService service, IApiResultMapper mapper, HttpContext http, CancellationToken token) =>
            Actor(http) is { } actor ? mapper.Map(await service.SelectAsync(actor, promptId, request, false, token).ConfigureAwait(false), http) : Results.Unauthorized())
            .WithTags("AiPrompts").WithName("aiPublishPromptVersion").Produces<AiPromptResponse>(200)
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .RequireAuthorization(FullNetPermissionPolicies.For(AiPromptPermissions.Publish));
        endpoints.MapPost(root + "/{promptId:guid}/rollback", async (Guid promptId, SelectAiPromptVersionRequest request, AiPromptManagementService service, IApiResultMapper mapper, HttpContext http, CancellationToken token) =>
            Actor(http) is { } actor ? mapper.Map(await service.SelectAsync(actor, promptId, request, true, token).ConfigureAwait(false), http) : Results.Unauthorized())
            .WithTags("AiPrompts").WithName("aiRollbackPromptVersion").Produces<AiPromptResponse>(200)
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .RequireAuthorization(FullNetPermissionPolicies.For(AiPromptPermissions.Rollback));
        endpoints.MapPost(root + "/{promptId:guid}/deactivate", async (Guid promptId, DeactivateAiPromptRequest request, AiPromptManagementService service, IApiResultMapper mapper, HttpContext http, CancellationToken token) =>
            Actor(http) is { } actor ? mapper.Map(await service.DeactivateAsync(actor, promptId, request, token).ConfigureAwait(false), http) : Results.Unauthorized())
            .WithTags("AiPrompts").WithName("aiDeactivatePrompt").Produces<AiPromptResponse>(200)
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .RequireAuthorization(FullNetPermissionPolicies.For(AiPromptPermissions.Deactivate));
    }
    private static Guid? Actor(HttpContext http) => http.User.Identity?.AuthenticationType is not ("FullNET.ApiKey" or "FullNET.Signature")
        && Guid.TryParse(http.User.FindFirst("sub")?.Value, out var actor) && actor != Guid.Empty ? actor : null;
}
