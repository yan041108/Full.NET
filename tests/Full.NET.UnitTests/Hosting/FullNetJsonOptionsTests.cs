using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
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
        Assert.IsNotNull(HostingJsonSerializerContext.Default.GetTypeInfo(typeof(Error)));
        Assert.AreEqual("{\"value\":\"ok\"}", json);
    }

    [TestMethod]
    public void AddFullNetJson_serializes_guid_as_lowercase_hyphenated_string()
    {
        using var provider = new ServiceCollection()
            .AddFullNetJson()
            .BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
        options.TypeInfoResolverChain.Insert(0, UnitTestJsonSerializerContext.Default);
        var identifier = Guid.Parse("019822d3-0700-7000-8000-000000000203");

        var json = JsonSerializer.Serialize(new GuidFixture(identifier), options);

        Assert.AreEqual(
            "{\"id\":\"019822d3-0700-7000-8000-000000000203\"}",
            json);
    }
}

internal sealed record JsonFixture(string Value);

internal sealed record GuidFixture(Guid Id);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(JsonFixture))]
[JsonSerializable(typeof(GuidFixture))]
internal partial class UnitTestJsonSerializerContext : JsonSerializerContext;
