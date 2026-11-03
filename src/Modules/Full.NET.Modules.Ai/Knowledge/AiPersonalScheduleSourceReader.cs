using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Calendar.Contracts;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>显式 Calendar 来源的事务外边界；完整身份保留在服务器，不接受展示引用代替。</summary>
internal sealed class AiPersonalScheduleSourceReader(IPersonalScheduleKnowledgeSource calendar,
    IDataTransactionState transactions, ICurrentTenant tenant)
{
    internal async Task<IReadOnlyList<PersonalScheduleKnowledgeSnapshot>?> CaptureAsync(
        Guid actor, IReadOnlyCollection<Guid> ids, CancellationToken token)
    {
        Outside(); var scope = tenant.Id; var scoped = AiKnowledgeScope.IsTenant(tenant);
        if (actor == Guid.Empty || ids.Count is < 1 or > PersonalScheduleKnowledgeSourceKeys.MaxSources
            || ids.Contains(Guid.Empty) || ids.Distinct().Count() != ids.Count) return null;
        var frozen = ids.ToArray();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(25));
        var rows = await AiKnowledgeIndexPdfAuthorityBoundary.RunAsync(
            work => calendar.CaptureAsync(frozen, work), deadline.Token).ConfigureAwait(false);
        Outside(); token.ThrowIfCancellationRequested();
        if (!SameScope() || rows is null || rows.Count != frozen.Length) return null;
        var captured = rows.ToArray();
        if (captured.Where((row, index) => row?.Proof is not { } proof || !Valid(proof, actor, scope) || proof.Id != frozen[index]
            || row.Content is not { Length: >= 1 and <= 256 }
            || row.StartAtUtc > row.EndAtUtc || row.Status is not ("pending" or "completed")).Any()) return null;
        // 外部响应集合不可在后续授权等待中改写；快照及证明本身均为不可变记录。
        var result = Array.AsReadOnly(captured);
        return result;
        bool SameScope() => tenant.Id == scope && AiKnowledgeScope.IsTenant(tenant) == scoped;
    }

    internal async Task<bool> RevalidateAsync(Guid actor, IReadOnlyCollection<PersonalScheduleKnowledgeProof> proofs, CancellationToken token,
        SessionBindingSnapshot? original = null)
    {
        Outside(); var scope = tenant.Id; var scoped = AiKnowledgeScope.IsTenant(tenant);
        var frozen = proofs.ToArray();
        if (actor == Guid.Empty || frozen.Length is < 1 or > PersonalScheduleKnowledgeSourceKeys.MaxSources
            || original is not null && (original.UserId != actor || original.TenantId != scope)
            || frozen.Any(proof => proof is null || !Valid(proof, actor, scope))
            || frozen.Select(proof => proof.Id).Distinct().Count() != frozen.Length) return false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(25));
        var allowed = await AiKnowledgeIndexPdfAuthorityBoundary.RunAsync(
            work => original is null ? calendar.VerifyAsync(frozen, work) : calendar.VerifyBackgroundAsync(original, frozen, work), deadline.Token).ConfigureAwait(false);
        Outside(); token.ThrowIfCancellationRequested();
        return allowed && tenant.Id == scope && AiKnowledgeScope.IsTenant(tenant) == scoped;
    }

    private void Outside()
    {
        if (transactions.HasTransaction) throw new InvalidOperationException("业务来源契约不能加入AI本地事务。");
    }

    private static bool Valid(PersonalScheduleKnowledgeProof proof, Guid actor, Guid? scope) =>
        proof.OwnerUserId == actor && proof.TenantId == scope && proof.Id != Guid.Empty && proof.Version > 0
        && proof.Kind == PersonalScheduleKnowledgeSourceKeys.Kind && proof.Purpose == PersonalScheduleKnowledgeSourceKeys.Purpose
        && proof.ProjectionHash is { Length: 64 } && proof.ProjectionHash.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
}
