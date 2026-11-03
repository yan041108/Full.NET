using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>输入身份必须完整出现；坏修订元数据不能被当作自动解析或重新读取当前头。</summary>
internal static class AiKnowledgeIndexCorrectionInput
{
    internal static bool IsValid(AiKnowledgeIndexDispatchRecord row) => row.CorrectionSourceId is null && row.CorrectionRevisionId is null
        && row.CorrectionHeadVersion is null && row.CorrectionSourceHash is null && row.CorrectionPayloadHash is null
        || row.CorrectionSourceId is { } source && source != Guid.Empty && row.CorrectionRevisionId is { } revision && revision != Guid.Empty
        && row.CorrectionHeadVersion is > 0 && Hash(row.CorrectionSourceHash) && Hash(row.CorrectionPayloadHash);
    internal static AiKnowledgeCorrectionIdentity? Identity(AiKnowledgeIndexDispatchRecord row) => IsValid(row) && row.CorrectionRevisionId.HasValue
        ? new(row.CorrectionSourceId!.Value, row.CorrectionRevisionId, row.CorrectionHeadVersion!.Value, row.CorrectionSourceHash!, row.CorrectionPayloadHash!) : null;
    private static bool Hash(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
