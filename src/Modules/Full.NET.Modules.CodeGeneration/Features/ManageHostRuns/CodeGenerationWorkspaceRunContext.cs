using System.Text.Json.Serialization;
using Full.NET.Modules.CodeGeneration.Contracts;
using Full.NET.Modules.CodeGeneration.Persistence;

namespace Full.NET.Modules.CodeGeneration.Features.ManageHostRuns;

/// <summary>冻结输入、审查人和可重建的运行摘要；只能从持有工作区租约的受控文件日志读取。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record CodeGenerationWorkspaceRunContext(int SchemaVersion, CodeGenerationRunRecord Run,
    Guid? PreviewRunId, DateTimeOffset ExpiresAtUtc, string GeneratorVersion, string? CanonicalSchemaJson,
    CodeGenerationIntegrationTargetRequest? IntegrationTarget, string PublicationContext);
