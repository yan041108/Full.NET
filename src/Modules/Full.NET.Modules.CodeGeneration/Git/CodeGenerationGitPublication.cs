using System.Text.Json.Serialization;

namespace Full.NET.Modules.CodeGeneration.Git;

/// <summary>本地提交与远端发布的独立恢复材料，正文与凭据不进入运行摘要。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record CodeGenerationGitPublication(Guid RunId, string Message, string Branch,
    string BaselineSha, string? ExpectedRemoteSha, string? LocalCommitSha, string Status,
    CodeGenerationGitPublicationFile[] Files)
{
    [JsonRequired]
    public int SchemaVersion { get; init; } = 1;
}

/// <summary>仅保存本次计划允许的路径与期望后态，null 摘要表示删除。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record CodeGenerationGitPublicationFile(string Path, string? Sha256, string? GitObjectSha = null);

/// <summary>文件已应用与 Git 发布分别表达，发布失败不丢弃本地提交。</summary>
internal sealed record CodeGenerationGitPublicationResult(string Status, string? CommitSha);
