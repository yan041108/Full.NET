namespace Full.NET.Modules.Ai.Domain;

/// <summary>知识库数据处理审批；网络允许不能替代分类及精确配置版本的业务批准。</summary>
internal static class AiKnowledgePolicy
{
    internal static string? Validate(string classification, Guid? embeddingId, int? embeddingVersion,
        Guid? generationId, int? generationVersion)
    {
        if (classification is not ("public" or "internal" or "restricted"))
            return "DataClassification must be public, internal or restricted.";
        if (!IsValidApproval(embeddingId, embeddingVersion) || !IsValidApproval(generationId, generationVersion))
            return "Each approval requires a non-empty model identifier and a positive model version, or both null.";
        return null;
    }

    internal static bool Allows(bool isEnabled, Guid? approvedId, int? approvedVersion, Guid modelId, int modelVersion) =>
        isEnabled && approvedId is { } id && id != Guid.Empty && approvedVersion is > 0
        && id == modelId && approvedVersion == modelVersion;

    // 分类本身不构成批准；撤销时标识与版本必须一起清空。
    private static bool IsValidApproval(Guid? id, int? version) =>
        id is null ? version is null : id != Guid.Empty && version is > 0;
}
