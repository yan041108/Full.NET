using Full.NET.Abstractions.Results;
using System.Text.Json.Serialization;
using Full.NET.Modules.Printing.Contracts;

namespace Full.NET.Modules.Printing.Serialization;

/// <summary>Printing 模块 JSON 源生成上下文。</summary>
[JsonSerializable(typeof(PagedResult<PrintingTemplateSummaryResponse>))]
[JsonSerializable(typeof(PagedResult<PrintingTemplateVersionSummaryResponse>))]
[JsonSerializable(typeof(PrintingFormSchemaDefinition))]
[JsonSerializable(typeof(PrintingFormFieldDefinition))]
[JsonSerializable(typeof(IReadOnlyList<PrintingFormSchemaDefinition>))]
[JsonSerializable(typeof(PrintingTemplateResponse))]
[JsonSerializable(typeof(CreatePrintingTemplateRequest))]
[JsonSerializable(typeof(UpdatePrintingTemplateRequest))]
[JsonSerializable(typeof(PublishPrintingTemplateRequest))]
[JsonSerializable(typeof(PrintingTemplateVersionResponse))]
[JsonSerializable(typeof(IReadOnlyList<PrintingTemplateResponse>))]
[JsonSerializable(typeof(IReadOnlyList<PrintingTemplateVersionResponse>))]
[JsonSerializable(typeof(PreviewPrintingTemplateRequest))]
[JsonSerializable(typeof(PrintingTemplatePreviewResponse))]
/// <summary>Printing 模块 JSON 源生成上下文；Native AOT 绑定已审计契约，禁止反射序列化。</summary>
internal partial class PrintingJsonSerializerContext : JsonSerializerContext;
