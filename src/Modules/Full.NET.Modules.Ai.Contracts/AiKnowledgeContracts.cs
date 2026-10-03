using System.Text.Json.Serialization;

namespace Full.NET.Modules.Ai.Contracts;

/// <summary>创建私有目录；租户和所有者均来自可信上下文，模型批准初始为空。</summary>
/// <param name="Name">目录名称，最多 200 个字符。</param>
/// <param name="Description">可选说明，最多 2000 个字符。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateAiKnowledgeBaseRequest(string Name, string? Description);

/// <summary>更新所属目录元数据，不允许修改模型处理批准。</summary>
/// <param name="Name">目录名称。</param>
/// <param name="Description">可选说明。</param>
/// <param name="IsEnabled">禁用后不得派发该目录的文档处理。</param>
/// <param name="Version">读取时的目录并发版本。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateAiKnowledgeBaseRequest(string Name, string? Description, bool IsEnabled, int Version);

/// <summary>明确批准指定模型配置及版本处理该目录文档；批准同时覆盖其目的地，模型变更须重新批准。</summary>
/// <param name="DataClassification">稳定分类：public、internal 或 restricted。</param>
/// <param name="EmbeddingModelConfigId">获准 Embedding 配置；为空表示拒绝。</param>
/// <param name="EmbeddingModelVersion">批准时的配置版本，与配置标识成对填写。</param>
/// <param name="GenerationModelConfigId">获准生成配置；为空表示拒绝。</param>
/// <param name="GenerationModelVersion">批准时的配置版本，与配置标识成对填写。</param>
/// <param name="Version">目录并发版本；分类/审批与元数据更新共享并发保护。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateAiKnowledgePolicyRequest(string DataClassification,
    Guid? EmbeddingModelConfigId, int? EmbeddingModelVersion,
    Guid? GenerationModelConfigId, int? GenerationModelVersion, int Version);

/// <summary>已授权私有目录及模型处理政策；不包含凭据、端点或原文。</summary>
/// <param name="Id">UUID v7 目录标识。</param>
/// <param name="Name">显示名称。</param>
/// <param name="Description">可选说明。</param>
/// <param name="IsEnabled">是否允许后续处理。</param>
/// <param name="DataClassification">稳定数据分类。</param>
/// <param name="EmbeddingModelConfigId">获准 Embedding 配置。</param>
/// <param name="EmbeddingModelVersion">批准的配置版本。</param>
/// <param name="GenerationModelConfigId">获准生成配置。</param>
/// <param name="GenerationModelVersion">批准的配置版本。</param>
/// <param name="CreatedAtUtc">创建时间 UTC。</param>
/// <param name="UpdatedAtUtc">最近变更时间 UTC。</param>
/// <param name="Version">目录乐观并发版本。</param>
public sealed record AiKnowledgeBaseResponse(Guid Id, string Name, string? Description, bool IsEnabled,
    string DataClassification, Guid? EmbeddingModelConfigId, int? EmbeddingModelVersion,
    Guid? GenerationModelConfigId, int? GenerationModelVersion,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc, int Version);
