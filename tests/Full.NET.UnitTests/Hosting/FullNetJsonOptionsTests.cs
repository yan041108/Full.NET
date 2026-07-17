using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Hosting.Serialization;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Hosting;

[TestClass]
public sealed class FullNetJsonOptionsTests
{
    [TestMethod]
    public void AddFullNetJson_uses_web_defaults_and_generated_context()
    {
        using var provider = new ServiceCollection()
            .AddFullNetJson()
            .BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
        options.TypeInfoResolverChain.Insert(0, UnitTestJsonSerializerContext.Default);

        var json = JsonSerializer.Serialize(new JsonFixture("ok"), options);

        Assert.AreEqual(JsonNamingPolicy.CamelCase, options.PropertyNamingPolicy);
        Assert.IsTrue(options.PropertyNameCaseInsensitive);
        Assert.IsTrue(options.TypeInfoResolverChain.Contains(
            HostingJsonSerializerContext.Default));
        Assert.AreEqual("{\"value\":\"ok\"}", json);
    }
}

internal sealed record JsonFixture(string Value);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(JsonFixture))]
internal partial class UnitTestJsonSerializerContext : JsonSerializerContext;
