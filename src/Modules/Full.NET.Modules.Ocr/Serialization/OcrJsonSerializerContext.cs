using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Ocr.Contracts;

namespace Full.NET.Modules.Ocr.Serialization;

/// <summary>OCR 模块 JSON 源生成上下文。</summary>
[JsonSerializable(typeof(OcrProviderConfigResponse))]
[JsonSerializable(typeof(UpdateOcrProviderConfigRequest))]
[JsonSerializable(typeof(TestOcrProviderConfigResult))]
[JsonSerializable(typeof(OcrIdCardTaskResponse))]
[JsonSerializable(typeof(CreateOcrIdCardTaskRequest))]
[JsonSerializable(typeof(ConfirmOcrIdCardTaskRequest))]
[JsonSerializable(typeof(PagedResult<OcrIdCardTaskResponse>))]
internal partial class OcrJsonSerializerContext : JsonSerializerContext;
