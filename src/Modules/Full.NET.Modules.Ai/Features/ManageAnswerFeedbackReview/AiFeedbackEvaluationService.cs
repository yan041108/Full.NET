using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Ai.Features.ManageAnswerFeedbackReview;

/// <summary>候选准备与显式准入均重新证明原会话和来源；不存储正文、不派发模型、不改变生产策略。</summary>
internal sealed class AiFeedbackEvaluationService(AiKnowledgeAnswerFeedbackReviewService reviews, AiFeedbackEvaluationToken tokens,
    ICurrentSessionAuthorization authorization, ICurrentTenant tenant, IDataTransactionState transactions, IClock clock)
{
    internal async Task<Result<AiFeedbackEvaluationCandidate>> PrepareAsync(Guid feedbackId, Guid actor,
        PrepareAiFeedbackEvaluationRequest request, CancellationToken token)
    {
        if (!AiFeedbackEvaluationPolicy.Valid(request.Sample) || request.FeedbackVersion < 1 || request.ReviewVersion < 1)
            return Failure<AiFeedbackEvaluationCandidate>(ErrorType.Validation);
        var scope = AiChatScope.Resolve(tenant);
        var session = await SessionAsync(actor, AiFeedbackEvaluationPermissions.Prepare, token).ConfigureAwait(false);
        if (session is null || !Same(scope)) return Failure<AiFeedbackEvaluationCandidate>(ErrorType.Forbidden);
        var row = await reviews.ProveEvaluationAsync(feedbackId, actor, request.FeedbackVersion, request.ReviewVersion,
            AiFeedbackEvaluationPermissions.Prepare, session.Value, token).ConfigureAwait(false);
        if (row is null || !Same(scope)) return Failure<AiFeedbackEvaluationCandidate>(ErrorType.NotFound);
        // 最后一次来源 SQL 已在原会话授权之后完成，不再等待身份查询而释放旧来源证明。
        token.ThrowIfCancellationRequested(); Outside();
        return Result<AiFeedbackEvaluationCandidate>.Success(tokens.Protect(new(1, actor, scope.TenantId, session.Value,
            feedbackId, request.FeedbackVersion, request.ReviewVersion, SourceHash(row), request.Sample, clock.UtcNow.AddMinutes(15))));
    }

    internal async Task<Result<AiFeedbackEvaluationAdmission>> AdmitAsync(Guid actor, AdmitAiFeedbackEvaluationRequest request, CancellationToken token)
    {
        if (!request.ConfirmManualRedaction) return Failure<AiFeedbackEvaluationAdmission>(ErrorType.Validation);
        var scope = AiChatScope.Resolve(tenant);
        var session = await SessionAsync(actor, AiFeedbackEvaluationPermissions.Admit, token).ConfigureAwait(false);
        if (session is null || !Same(scope)) return Failure<AiFeedbackEvaluationAdmission>(ErrorType.Forbidden);
        var envelope = tokens.Read(request.CandidateToken, actor, scope.TenantId, session.Value);
        if (envelope is null) return Failure<AiFeedbackEvaluationAdmission>(ErrorType.Validation);
        var row = await reviews.ProveEvaluationAsync(envelope.FeedbackId, actor, envelope.FeedbackVersion, envelope.ReviewVersion,
            AiFeedbackEvaluationPermissions.Admit, session.Value, token).ConfigureAwait(false);
        if (row is null || SourceHash(row) != envelope.SourceBindingHash || !Same(scope)) return Failure<AiFeedbackEvaluationAdmission>(ErrorType.NotFound);
        if (!Same(scope) || tokens.Read(request.CandidateToken, actor, scope.TenantId, session.Value) != envelope)
            return Failure<AiFeedbackEvaluationAdmission>(ErrorType.Forbidden);
        token.ThrowIfCancellationRequested(); Outside();
        var hash = AiFeedbackEvaluationPolicy.Hash(envelope.Sample);
        return Result<AiFeedbackEvaluationAdmission>.Success(new(1,
            AiFeedbackEvaluationPolicy.Digest($"feedback-evaluation.v1|{envelope.SourceBindingHash}|{hash}"), hash, envelope.Sample, clock.UtcNow));
    }

    private async Task<Guid?> SessionAsync(Guid actor, string permission, CancellationToken token)
    {
        Outside(); token.ThrowIfCancellationRequested();
        var current = await authorization.AuthorizeAsync(permission, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested(); Outside();
        return current is not null && current.UserId == actor && current.TenantId == tenant.Id && current.SessionId != Guid.Empty
            ? current.SessionId : null;
    }
    private static string SourceHash(AiKnowledgeAnswerFeedbackReviewRecord row) => AiFeedbackEvaluationPolicy.Digest(
        FormattableString.Invariant($"feedback-source.v1|{row.FeedbackId:N}|{row.MessageId:N}|{row.SessionId:N}|{row.OwnerUserId:N}|{row.FeedbackVersion}|{row.ReviewVersion}|{row.ReviewerUserId:N}|{row.LineageHash}|{row.RatingKey}|{row.ReasonKey}|{row.CitationIdsJson}"));
    private bool Same(AiChatScope scope) => AiChatScope.Resolve(tenant) == scope;
    private void Outside() { if (transactions.HasTransaction) throw new InvalidOperationException("Feedback evaluation authorization cannot join an ambient transaction."); }
    private static Result<T> Failure<T>(ErrorType type) => Result<T>.Failure(new(type == ErrorType.Forbidden
        ? "authorization.permission_denied" : type == ErrorType.Validation ? AiKnowledgeErrorCodes.FeedbackInvalid : AiKnowledgeErrorCodes.FeedbackNotFound,
        "The feedback evaluation candidate is unavailable or invalid.", type));
}
