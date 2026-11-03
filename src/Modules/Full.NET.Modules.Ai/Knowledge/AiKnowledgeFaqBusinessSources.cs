using Full.NET.Modules.Calendar.Contracts;
using Full.NET.Modules.Ai.Contracts;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>只合并同一当前主体的准确业务证明；不因只作证明而丢掉未引用输入或祖先来源。</summary>
internal static class AiKnowledgeFaqBusinessSources
{
    internal static IReadOnlyList<PersonalScheduleKnowledgeProof>? Freeze(IEnumerable<PersonalScheduleKnowledgeProof>? values)
    {
        if (values is null) return null;
        var result = new Dictionary<Guid, PersonalScheduleKnowledgeProof>();
        foreach (var value in values)
        {
            if (value is null || !AiPersonalScheduleAnswerSources.Valid(value, value.OwnerUserId, value.TenantId)
                || result.TryGetValue(value.Id, out var old) && old != value) throw Invalid();
            result[value.Id] = value;
        }
        if (result.Count is < 1 or > 7 || result.Values.Select(value => (value.OwnerUserId, value.TenantId)).Distinct().Count() != 1) throw Invalid();
        return Array.AsReadOnly(result.Values.OrderBy(value => value.Id.ToString("N"), StringComparer.Ordinal).ToArray());
    }

    internal static IReadOnlyList<PersonalScheduleKnowledgeProof> For(AiKnowledgeRetrievalSnapshot snapshot)
    {
        var values = snapshot.PersonalSchedules.Select(value => value.Proof)
            .Concat(snapshot.FaqSources.Values.SelectMany(value => value.PersonalSchedules ?? []));
        var result = values.ToArray(); return result.Length == 0 ? [] : Freeze(result)!;
    }

    internal static bool Same(IReadOnlyList<PersonalScheduleKnowledgeProof>? first, IReadOnlyList<PersonalScheduleKnowledgeProof>? second) =>
        first is null ? second is null : second is not null && first.SequenceEqual(second);

    internal static IReadOnlyList<AiPersonalScheduleSourceSelection>? Public(AiKnowledgeFaqProvenance origin) => origin.PersonalSchedules is null
        ? null : Array.AsReadOnly(origin.PersonalSchedules.Select(value => new AiPersonalScheduleSourceSelection(value.Id, value.Version, value.ProjectionHash)).ToArray());

    internal static void Validate(IReadOnlyList<PersonalScheduleKnowledgeProof>? values, int documents, AiKnowledgeFaqGraph? graph = null)
    {
        if (values is null)
        { if (graph?.Nodes.Any(value => value.Source.PersonalSchedules is not null) == true) throw Invalid(); return; }
        var frozen = Freeze(values)!;
        if (!values.SequenceEqual(frozen) || documents + (graph?.Nodes.Count ?? 0) + values.Count > 8
            || graph?.Nodes.SelectMany(node => node.Source.PersonalSchedules ?? []).Any(value => !values.Contains(value)) == true) throw Invalid();
    }
    internal static InvalidDataException Invalid() => new("FAQ 业务来源缺失、冲突、跨所属人或超过完整依赖预算。");
}
