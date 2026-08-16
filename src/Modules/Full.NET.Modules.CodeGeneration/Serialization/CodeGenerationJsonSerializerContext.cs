using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.CodeGeneration.Contracts;

namespace Full.NET.Modules.CodeGeneration.Serialization;

/// <summary>
/// 为代码生成预览 HTTP 契约提供 AOT 友好的 System.Text.Json 元数据。
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(CodeGenerationPreviewColumnRequest))]
[JsonSerializable(typeof(CodeGenerationPreviewColumnUiRequest))]
[JsonSerializable(typeof(CodeGenerationCatalogTableResponse))]
[JsonSerializable(typeof(IReadOnlyList<CodeGenerationCatalogTableResponse>))]
[JsonSerializable(typeof(CodeGenerationCatalogColumnListResponse))]
[JsonSerializable(typeof(CodeGenerationCatalogColumnSyncRequest))]
[JsonSerializable(typeof(CodeGenerationCatalogColumnSyncResponse))]
[JsonSerializable(typeof(CodeGenerationEntityCapabilitiesRequest))]
[JsonSerializable(typeof(CodeGenerationTemplateResponse))]
[JsonSerializable(typeof(CreateCodeGenerationTemplateRequest))]
[JsonSerializable(typeof(DeleteCodeGenerationTemplateRequest))]
[JsonSerializable(typeof(PagedResult<CodeGenerationTemplateResponse>))]
[JsonSerializable(typeof(CodeGenerationPreviewRequest))]
[JsonSerializable(typeof(CodeGenerationPreviewResponse))]
[JsonSerializable(typeof(CodeGenerationRunPreviewRequest))]
[JsonSerializable(typeof(CodeGenerationRunPreviewResponse))]
[JsonSerializable(typeof(CodeGenerationRunApplyRequest))]
[JsonSerializable(typeof(CodeGenerationIntegrationTargetRequest))]
[JsonSerializable(typeof(CodeGenerationClientRouteTargetRequest))]
[JsonSerializable(typeof(CodeGenerationRunApplyResponse))]
[JsonSerializable(typeof(CodeGenerationRunRollbackRequest))]
[JsonSerializable(typeof(CodeGenerationRunRollbackResponse))]
[JsonSerializable(typeof(CodeGenerationRunRollbackChainRequest))]
[JsonSerializable(typeof(CodeGenerationRunRollbackChainResponse))]
[JsonSerializable(typeof(CodeGenerationRunResponse))]
[JsonSerializable(typeof(PagedResult<CodeGenerationRunResponse>))]
[JsonSerializable(typeof(CodeGenerationRelationshipRequest))]
[JsonSerializable(typeof(UpdateCodeGenerationTemplateRequest))]
internal partial class CodeGenerationJsonSerializerContext
    : JsonSerializerContext;
