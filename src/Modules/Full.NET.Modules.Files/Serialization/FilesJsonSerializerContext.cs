using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Files.Contracts;

namespace Full.NET.Modules.Files.Serialization;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(HostFileResponse))]
[JsonSerializable(typeof(PagedResult<HostFileResponse>))]
[JsonSerializable(typeof(UpdateHostFileMetadataRequest))]
[JsonSerializable(typeof(HostFileReferenceClaimResponse))]
[JsonSerializable(typeof(PagedResult<HostFileReferenceClaimResponse>))]
[JsonSerializable(typeof(HostFolderTreeNode))]
[JsonSerializable(typeof(HostFolderTreeNode[]))]
[JsonSerializable(typeof(HostFolderResponse))]
[JsonSerializable(typeof(CreateHostFolderRequest))]
[JsonSerializable(typeof(UpdateHostFolderRequest))]
[JsonSerializable(typeof(DeleteHostFolderRequest))]
internal partial class FilesJsonSerializerContext : JsonSerializerContext;
