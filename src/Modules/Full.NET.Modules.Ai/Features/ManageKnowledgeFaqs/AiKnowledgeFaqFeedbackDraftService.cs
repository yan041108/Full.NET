using Full.NET.Abstractions.Results;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Knowledge;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeFaqs;

internal sealed partial class AiKnowledgeFaqDraftService
{
    internal Task<Result<AiKnowledgeFaqFeedbackPreview>> PreviewFeedbackAsync(Guid knowledge, Guid actor,
        PreviewAiKnowledgeFaqFeedback request, CancellationToken token) => Run(actor, AiKnowledgeFaqPermissions.Feedback, async (context, work) =>
    {
        AiKnowledgeFaqDraftPolicy.Key(request.FeedbackId);
        var prepared = await feedback.PrepareAsync(knowledge, request.FeedbackId, actor, work).ConfigureAwait(false); Check(context);
        return prepared is null ? Unavailable<AiKnowledgeFaqFeedbackPreview>() : Result<AiKnowledgeFaqFeedbackPreview>.Success(new(
            AiKnowledgeFaqFeedbackPreparation.Digest(prepared, actor), AiKnowledgeFaqDraftPolicy.Hash(AiKnowledgeFaqProvenanceCodec.Encode(prepared.Origin)), prepared.Origin.Feedback!.Public(), prepared.Answer,
            AiKnowledgeFaqGraphs.Public(prepared.Origin), AiKnowledgeFaqGraphs.PublicGraph(prepared.Origin.Graph))
            { PersonalSchedules = AiKnowledgeFaqBusinessSources.Public(prepared.Origin) });
    }, token);

    internal Task<Result<AiKnowledgeFaqDraftMutation>> FeedbackAsync(Guid knowledge, Guid actor,
        CreateAiKnowledgeFaqFeedbackDraft request, CancellationToken token) => Run(actor, AiKnowledgeFaqPermissions.Feedback, async (context, work) =>
    {
        AiKnowledgeFaqDraftPolicy.Key(request.RequestKey); AiKnowledgeFaqDraftPolicy.Key(request.FeedbackId);
        if (request.Content is null || request.Digest is not { Length: 64 } || !request.Digest.All(Uri.IsHexDigit)) return Invalid<AiKnowledgeFaqDraftMutation>();
        var prepared = await feedback.PrepareAsync(knowledge, request.FeedbackId, actor, work).ConfigureAwait(false); Check(context);
        if (prepared is null) return Unavailable<AiKnowledgeFaqDraftMutation>();
        if (request.Digest != AiKnowledgeFaqFeedbackPreparation.Digest(prepared, actor)) return Conflict<AiKnowledgeFaqDraftMutation>();
        var input = request.Content;
        var content = AiKnowledgeFaqDraftPolicy.Encode(new(input.Question, input.Aliases, prepared.Answer, input.Category, input.ValidFromUtc, input.ExpiresAtUtc));
        return await Create(knowledge, context, request.RequestKey, content, prepared.Origin, null, work).ConfigureAwait(false);
    }, token);
}
