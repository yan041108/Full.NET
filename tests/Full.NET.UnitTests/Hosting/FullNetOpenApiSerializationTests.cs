using System.Text.Json;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
using Full.NET.Hosting.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Hosting;

/// <summary>文件与流虽然不走 JSON 请求绑定，OpenAPI 的静态 Schema 导出仍需要其声明类型元数据。</summary>
[TestClass]
public sealed class FullNetOpenApiSerializationTests
{
    [TestMethod]
    public void Multipart_and_stream_schema_roots_are_available_without_reflection()
    {
        using var services = new ServiceCollection().AddFullNetJson().BuildServiceProvider();
        var options = services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
        foreach (var resolver in options.TypeInfoResolverChain.OfType<DefaultJsonTypeInfoResolver>().ToArray())
            options.TypeInfoResolverChain.Remove(resolver);
        foreach (var type in new[] { typeof(IFormFile), typeof(IFormFileCollection), typeof(Stream) })
        {
            Assert.IsNotNull(HostingJsonSerializerContext.Default.GetTypeInfo(type));
            Assert.IsNotNull(options.GetJsonSchemaAsNode(type));
        }
    }
}
