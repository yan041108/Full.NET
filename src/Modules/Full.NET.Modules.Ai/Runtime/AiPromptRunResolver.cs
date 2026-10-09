using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Agents.Definitions;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Features.ManagePromptVersions;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Ai.Runtime;

/// <summary>创建与执行共同证明精确版本；当前授权在事务外，发布切换不重写旧运行，停用代次不可复活。</summary>
internal sealed class AiPromptRunResolver(AiPromptReader reader, ICurrentTenant tenant,
    IBackgroundSessionAuthorization authorization, IDataTransactionState transactions)
{
    internal async Task<bool> AuthorizedAsync(SessionBindingSnapshot binding, CancellationToken token)
    {
        if (transactions.HasTransaction) throw new InvalidOperationException("Prompt 使用授权不能加入已有事务。");
        foreach (var code in new[] { AiAgentRunPermissions.Read, AiAgentRunPermissions.Create, AiPromptPermissions.Use })
        {
            var actor = await authorization.AuthorizeAsync(binding, code, token).ConfigureAwait(false);
            if (actor?.UserId != binding.UserId || actor.TenantId != binding.TenantId || actor.SessionId != binding.SessionId
                || binding.TenantId != tenant.Id || (binding.TenantId.HasValue ? tenant.IsHost : !tenant.IsHost)) return false;
        }
        return true;
    }

    internal async Task<Result<AiPromptRunPreparation>> PrepareAsync(CreateAiAgentRunRequest request, SessionBindingSnapshot binding, CancellationToken token)
    {
        if (!await AuthorizedAsync(binding, token).ConfigureAwait(false)) return Failure<AiPromptRunPreparation>("authorization.permission_denied", ErrorType.Forbidden);
        var scope = AiChatScope.Resolve(tenant);
        var version = await reader.VersionAsync(scope, request.PromptVersionId!.Value, token).ConfigureAwait(false);
        var body = version is null ? null : AiPromptVersionIntegrity.Decode(version);
        if (version is null || body is null) return Failure<AiPromptRunPreparation>("ai.prompts.unavailable", ErrorType.NotFound);
        var head = await reader.HeadAsync(scope, version.PromptId, token).ConfigureAwait(false);
        if (!AiPromptReader.ValidHead(scope, head) || head!.ActiveVersionId != version.Id || !version.WasPublished
            || version.TenantId != scope.TenantId || version.ScopeKey != (scope.TenantId?.ToString("N") ?? "host")) return Failure<AiPromptRunPreparation>("ai.prompts.unavailable", ErrorType.NotFound);
        if (!ValidInput(request.DefinitionKey, request.ModelConfigId, request.Prompt, request.InputTokenLimit, request.OutputTokenLimit, body))
            return Failure<AiPromptRunPreparation>("ai.prompts.invalid", ErrorType.Validation);
        var model = await reader.ModelAsync(scope, body.ModelConfigId, token).ConfigureAwait(false);
        if (model?.Id != body.ModelConfigId || !Matches(model, version) || !await AuthorizedAsync(binding, token).ConfigureAwait(false)) return Failure<AiPromptRunPreparation>("ai.prompts.unavailable", ErrorType.NotFound);
        return Result<AiPromptRunPreparation>.Success(new(new(head.Id, version.Id, version.Version, version.BodyHash, head.Epoch), model!));
    }

    internal async Task<Result<string>> ExecutionAsync(AgentRunBudgetSnapshot snapshot, string definition, SessionBindingSnapshot binding, AiModelConfigRecord boundModel, CancellationToken token)
    {
        if (!await AuthorizedAsync(binding, token).ConfigureAwait(false)) return Failure<string>("authorization.permission_denied", ErrorType.Forbidden);
        var saved = snapshot.PromptBinding;
        if (saved is null || saved.PromptId == Guid.Empty || saved.VersionId == Guid.Empty || saved.Version < 1 || saved.Epoch < 0) return Failure<string>("ai.prompts.unavailable", ErrorType.NotFound);
        var scope = AiChatScope.Resolve(tenant);
        var version = await reader.VersionAsync(scope, saved.VersionId, token).ConfigureAwait(false);
        var body = version is null ? null : AiPromptVersionIntegrity.Decode(version);
        var head = await reader.HeadAsync(scope, saved.PromptId, token).ConfigureAwait(false);
        if (body is null || version!.PromptId != saved.PromptId || version.Version != saved.Version || version.BodyHash != saved.BodyHash
            || !version.WasPublished || !AiPromptReader.ValidHead(scope, head) || head!.ActiveVersionId is null || head.Epoch != saved.Epoch
            || version.TenantId != scope.TenantId || version.ScopeKey != (scope.TenantId?.ToString("N") ?? "host")
            || !ValidInput(definition, snapshot.ModelConfigId, snapshot.Prompt, snapshot.InputTokenLimit, snapshot.OutputTokenLimit, body)) return Failure<string>("ai.prompts.unavailable", ErrorType.NotFound);
        var model = await reader.ModelAsync(scope, body.ModelConfigId, token).ConfigureAwait(false);
        // 再查权威模型，拒绝客户端建立期间模型修订变化；旧凭据身份不能执行新策略。
        if (model?.Id != body.ModelConfigId || !Matches(model, version) || boundModel.Id != body.ModelConfigId || !Matches(boundModel, version)
            || !await AuthorizedAsync(binding, token).ConfigureAwait(false)) return Failure<string>("ai.prompts.unavailable", ErrorType.NotFound);
        return Result<string>.Success(AiPromptPolicy.Compose(body, snapshot.Prompt)!);
    }

    private static bool ValidInput(string definition, Guid model, string prompt, long input, long output, AiPromptBody body) =>
        definition == AgentDefinitionRegistry.SingleTextKey && model == body.ModelConfigId
        && input > 0 && input <= body.InputTokenLimit && output > 0 && output <= body.OutputTokenLimit && AiPromptPolicy.Compose(body, prompt) is not null;
    private static bool Matches(AiModelConfigRecord? model, AiPromptVersionRecord version) => model is { IsEnabled: true }
        && model.Version == version.ModelConfigVersion && model.ProviderKey == version.ProviderKey && model.ModelId == version.ModelId;
    private static Result<T> Failure<T>(string code, ErrorType type) => Result<T>.Failure(new Error(code, "The published Prompt version is unavailable.", type));
}

/// <summary>准备结果只在本模块使用，不暴露模型凭据或数据访问类型。</summary>
internal sealed record AiPromptRunPreparation(AiPromptRunBinding Binding, AiModelConfigRecord Model);
