using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Printing.Contracts;

namespace Full.NET.Modules.Printing.Serialization;

/// <summary>Printing 模块 JSON 源生成上下文。</summary>
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
internal partial class PrintingJsonSerializerContext : JsonSerializerContext;
