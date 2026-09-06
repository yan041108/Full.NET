using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Platform.Contracts;

namespace Full.NET.Modules.Platform.Serialization;

/// <summary>Platform 模块 JSON 源生成上下文，覆盖全部公开 DTO。</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(CreateHostReleaseNoteRequest))]
[JsonSerializable(typeof(DeleteHostReleaseNoteRequest))]
[JsonSerializable(typeof(HostReleaseNoteResponse))]
[JsonSerializable(typeof(MyReleaseNoteResponse))]
[JsonSerializable(typeof(PagedResult<HostReleaseNoteResponse>))]
[JsonSerializable(typeof(PagedResult<MyReleaseNoteResponse>))]
[JsonSerializable(typeof(PublishHostReleaseNoteRequest))]
[JsonSerializable(typeof(RetractHostReleaseNoteRequest))]
[JsonSerializable(typeof(UpdateHostReleaseNoteRequest))]
internal partial class PlatformJsonSerializerContext
    : JsonSerializerContext;
