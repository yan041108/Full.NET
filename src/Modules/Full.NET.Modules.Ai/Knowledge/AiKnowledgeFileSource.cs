using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>文件协议显式拒绝数据库修订来源；缺失文件不能用空 UUID 或修订身份冒充。</summary>
internal static class AiKnowledgeFileSource
{
    internal static Guid RequireFileId(AiKnowledgeIndexStatusRecord source) => source.FaqRevisionId is null
        && source.FileId is { } file && file != Guid.Empty ? file : throw new InvalidDataException("The source is not a file protocol.");

    internal static Guid RequireFileId(AiKnowledgeIndexDispatchRecord source) => source.FaqRevisionId is null
        && source.FileId is { } file && file != Guid.Empty ? file : throw new InvalidDataException("The source is not a file protocol.");
}
