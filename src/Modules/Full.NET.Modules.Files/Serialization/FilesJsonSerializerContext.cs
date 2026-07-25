using System.Text.Json;
using System.Text.Json.Serialization;

using Full.NET.Abstractions.Results;

using Full.NET.Modules.Files.Contracts;



namespace Full.NET.Modules.Files.Serialization;



[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]

[JsonSerializable(typeof(HostFileResponse))]

[JsonSerializable(typeof(PagedResult<HostFileResponse>))]

internal partial class FilesJsonSerializerContext : JsonSerializerContext;

