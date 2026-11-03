using System.Text.Json.Serialization;

namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>工作区全局顺序。Revision 每次提交递增，即使回滚到相同内容也不能重用旧计划。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GenerationWorkspaceHead(int SchemaVersion, Guid WorkspaceId, long Revision, Guid? TipOperationId, Guid? LastOperationId);

/// <summary>发生变化的单个文件；原文在首次写盘前保存，缺失用 null 表达。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GenerationOperationFile(string RelativePath, string? Before, string? After);

/// <summary>未改动的受管文件也参与漂移校验，但无需重复备份正文。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GenerationOperationCheck(string RelativePath, string? Sha256);

/// <summary>可独立恢复的整链操作记录；Context 只由已知宿主解释，不执行其中的代码。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GenerationWorkspaceOperation(
    int SchemaVersion, Guid Id, Guid IdempotencyKey, string Kind, string Client, string? Context,
    GenerationWorkspaceHead PreviousHead, GenerationWorkspaceHead NextHead, string Phase,
    GenerationOperationFile[] Files, GenerationOperationCheck[] Checks, string PlanSha256);
