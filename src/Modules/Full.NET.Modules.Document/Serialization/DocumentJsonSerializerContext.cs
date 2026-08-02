using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Document.Contracts;

namespace Full.NET.Modules.Document.Serialization;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(HostDocumentItemResponse))]
[JsonSerializable(typeof(HostDocumentVersionResponse))]
[JsonSerializable(typeof(CreateHostDocumentItemRequest))]
[JsonSerializable(typeof(UpdateHostDocumentItemRequest))]
[JsonSerializable(typeof(AddHostDocumentVersionRequest))]
[JsonSerializable(typeof(DeleteHostDocumentItemRequest))]
[JsonSerializable(typeof(RestoreHostDocumentItemRequest))]
[JsonSerializable(typeof(PagedResult<HostDocumentItemResponse>))]
[JsonSerializable(typeof(HostDocumentCategoryResponse))]
[JsonSerializable(typeof(CreateHostDocumentCategoryRequest))]
[JsonSerializable(typeof(UpdateHostDocumentCategoryRequest))]
[JsonSerializable(typeof(DeleteHostDocumentCategoryRequest))]
[JsonSerializable(typeof(HostDocumentTagResponse))]
[JsonSerializable(typeof(CreateHostDocumentTagRequest))]
[JsonSerializable(typeof(UpdateHostDocumentTagRequest))]
[JsonSerializable(typeof(DeleteHostDocumentTagRequest))]
[JsonSerializable(typeof(IReadOnlyList<HostDocumentCategoryResponse>))]
[JsonSerializable(typeof(IReadOnlyList<HostDocumentTagResponse>))]
internal partial class DocumentJsonSerializerContext : JsonSerializerContext;
