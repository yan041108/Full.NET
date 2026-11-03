using System.Text.Json;
using System.Text.Json.Schema;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Serialization;

namespace Full.NET.UnitTests.Document;

/// <summary>列表查询的可空枚举必须独立进入静态 Schema 闭包，不能依赖 JIT 的反射回退。</summary>
[TestClass]
public sealed class DocumentOpenApiSerializationTests
{
    [TestMethod]
    public void Nullable_document_filter_schemas_are_available_without_reflection()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = DocumentJsonSerializerContext.Default
        };
        foreach (var type in new[] { typeof(HostDocumentStatus?), typeof(HostDocumentType?) })
        {
            Assert.IsNotNull(DocumentJsonSerializerContext.Default.GetTypeInfo(type));
            Assert.IsNotNull(options.GetJsonSchemaAsNode(type));
        }
    }
}
