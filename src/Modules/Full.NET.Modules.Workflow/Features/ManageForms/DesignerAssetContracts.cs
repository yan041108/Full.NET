using System.Text.Json;
namespace Full.NET.Modules.Workflow.Features.ManageForms;

/// <summary>完整原版设计稿，来源与格式须匹配；草稿修订覆盖整个表单资产。</summary>
internal sealed record SaveWorkflowDesignerAssetRequest(long ExpectedRevision, string SourceCommit, int FormatVersion, JsonElement DesignerJson);

/// <summary>保存后的完整设计资产；摘要由服务端对完整内容计算。</summary>
internal sealed record WorkflowDesignerAssetResponse(Guid FormId, long DraftRevision, string SourceCommit, int FormatVersion, JsonElement DesignerJson, string ContentHash);
