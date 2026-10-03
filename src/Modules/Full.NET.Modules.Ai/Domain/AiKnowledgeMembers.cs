namespace Full.NET.Modules.Ai.Domain;

/// <summary>有界整量成员替换；所有者隐式持有，不进入可撤销的成员集合。</summary>
internal static class AiKnowledgeMembers
{
    internal static string? Validate(Guid owner, IReadOnlyCollection<Guid>? userIds, int version) =>
        owner == Guid.Empty || version < 1 || userIds is null || userIds.Count > 100
            || userIds.Any(id => id == Guid.Empty || id == owner) || userIds.Distinct().Count() != userIds.Count
            ? "Members must be unique non-empty identifiers, exclude the owner, and contain at most 100 users; version must be positive."
            : null;
}
