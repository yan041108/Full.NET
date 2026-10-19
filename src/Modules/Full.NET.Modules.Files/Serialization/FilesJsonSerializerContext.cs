using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Files.Contracts;

// Files 模块 Native AOT 友好的 System.Text.Json 源生成上下文；仅注册已审计的契约类型。
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
[JsonSerializable(typeof(BatchDeleteHostFilesRequest))]
[JsonSerializable(typeof(BatchDeleteHostFilesResponse))]
[JsonSerializable(typeof(BatchDeleteHostFileItem))]
[JsonSerializable(typeof(BatchUploadHostFilesResponse))]
[JsonSerializable(typeof(BatchUploadHostFileItem))]
[JsonSerializable(typeof(StorageProviderCatalogItem))]
[JsonSerializable(typeof(StorageProviderCatalogItem[]))]
[JsonSerializable(typeof(TestStorageProviderConnectivityResult))]
/// <summary>Files 模块 JSON 源生成上下文；Native AOT 绑定已审计契约，禁止反射序列化。</summary>
internal partial class FilesJsonSerializerContext : JsonSerializerContext;
