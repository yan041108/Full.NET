using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Calendar.Contracts;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>冻结完整业务选择及证明，所有检查均保留未被最终引用的输入。</summary>
internal static class AiPersonalScheduleAnswerSources
{
    internal static IReadOnlyList<AiPersonalScheduleSourceSelection>? Freeze(IReadOnlyList<AiPersonalScheduleSourceSelection>? values)
    {
        if (values is null) return null;
        if (values.Count is < 1 or > 7) throw Invalid();
        var frozen = values.ToArray();
        if (frozen.Any(value => value is null || value.Id == Guid.Empty || value.Version < 1 || !Hash(value.ProjectionHash))
            || frozen.Select(value => value.Id).Distinct().Count() != frozen.Length) throw Invalid();
        return Array.AsReadOnly(frozen);
    }

    internal static IReadOnlyList<PersonalScheduleKnowledgeSnapshot> FreezeSnapshots(
        IReadOnlyList<PersonalScheduleKnowledgeSnapshot>? values, Guid actor, Guid? tenant)
    {
        if (values is null || values.Count == 0) return [];
        if (values.Count > 7) throw Invalid();
        var result = values.ToArray();
        if (result.Any(value => value is null || !Valid(value.Proof, actor, tenant)
            || value.Content is not { Length: >= 1 and <= 256 } || value.StartAtUtc > value.EndAtUtc
            || value.Status is not ("pending" or "completed")) || result.Select(value => value.Proof.Id).Distinct().Count() != result.Length) throw Invalid();
        return Array.AsReadOnly(result);
    }

    internal static bool Valid(PersonalScheduleKnowledgeProof? proof, Guid actor, Guid? tenant) =>
        proof is not null && proof.Kind == PersonalScheduleKnowledgeSourceKeys.Kind
        && proof.Purpose == PersonalScheduleKnowledgeSourceKeys.Purpose && proof.Id != Guid.Empty
        && proof.TenantId == tenant && tenant != Guid.Empty && proof.OwnerUserId == actor && actor != Guid.Empty
        && proof.Version > 0 && Hash(proof.ProjectionHash);

    internal static void RequireBudget(int knowledgeDependencies, int schedules)
    {
        if (knowledgeDependencies is < 1 or > 8 || schedules is < 0 or > 7 || knowledgeDependencies + schedules > 8) throw Invalid();
    }

    // 候选、FAQ 祖先及仅核证的文档叶按真实文档身份联合计数，不能因不入候选而省略。
    internal static int CountKnowledgeDependencies(AiKnowledgeRetrievalSnapshot snapshot) => snapshot.Sources.Select(value => value.DocumentId)
        .Concat(snapshot.FaqSources.Values.SelectMany(value => value.Dependencies).Select(value => value.DocumentId))
        .Concat(snapshot.FaqSources.Values.Where(value => value.Graph is not null).SelectMany(value => value.Graph!.Nodes).Select(value => value.Status.DocumentId))
        .Distinct().Count();

    internal static int CountKnowledgeDependencies(AiKnowledgeAnswerLineage lineage) => lineage.Dependencies.Select(value => value.DocumentId)
        .Concat(lineage.Dependencies.Where(value => value.Faq is not null).SelectMany(value => value.Faq!.Dependencies).Select(value => value.DocumentId))
        .Concat(lineage.Dependencies.Where(value => value.Faq?.Graph is not null).SelectMany(value => value.Faq!.Graph!.Nodes).Select(value => value.Status.DocumentId))
        .Distinct().Count();

    private static bool Hash(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
    private static InvalidDataException Invalid() => new("日程选择、完整来源身份或联合来源预算无效。");
}
