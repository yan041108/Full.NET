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
[JsonSerializable(typeof(BatchDeleteHostFilesRequest))]
[JsonSerializable(typeof(BatchDeleteHostFilesResponse))]
[JsonSerializable(typeof(BatchDeleteHostFileItem))]
[JsonSerializable(typeof(BatchUploadHostFilesResponse))]
[JsonSerializable(typeof(BatchUploadHostFileItem))]
[JsonSerializable(typeof(StorageProviderCatalogItem))]
[JsonSerializable(typeof(StorageProviderCatalogItem[]))]
[JsonSerializable(typeof(TestStorageProviderConnectivityResult))]
// OpenAPI 使用端点声明的集合接口类型，数组元数据不能替代集合根类型。
[JsonSerializable(typeof(IReadOnlyList<StorageProviderCatalogItem>))]
internal partial class FilesJsonSerializerContext : JsonSerializerContext;
