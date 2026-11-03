using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.CodeGeneration.Integration;

namespace Full.NET.UnitTests.CodeGeneration;

/// <summary>应用业务客户端必须和共享 HTTP 运行时分离，模块文本不能注入生成源码。</summary>
[TestClass]
public sealed class CrudApplicationClientTests
{
    [TestMethod]
    public void Application_operations_use_the_explicit_module_and_keep_shared_http()
    {
        var schema = FullNetCrudSchemaTests.CreateProductSchema();
        var source = CrudArtifactGenerator.GenerateVueClient(schema, "./catalog-client/index.generated.js");
        StringAssert.Contains(source, "import type { HttpClient } from '@fullnet/client-contracts';");
        Assert.AreEqual(2, source.Split("from './catalog-client/index.generated.js';").Length - 1);
        Assert.AreEqual(1, source.Split("from '@fullnet/client-contracts';").Length - 1);
        StringAssert.Contains(source, "catalogCreateProduct");
        StringAssert.Contains(source, "createProductsApi");
        Assert.IsFalse(source.Contains("fetch(", StringComparison.Ordinal));
        var unchanged = CrudArtifactGenerator.Generate(schema).Single(artifact =>
            artifact.RelativePath == "clients/vue/products.generated.ts").Content;
        Assert.IsFalse(unchanged.Contains("catalog-client", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(" ./client.js")]
    [DataRow("./client.js' ; alert(1)")]
    [DataRow("https://example.com/client.js")]
    [DataRow("C:\\client.js")]
    [DataRow("/client.js")]
    [DataRow("./client//index.js")]
    [DataRow("./client.js\n")]
    public void Invalid_module_is_rejected_before_artifact_generation(string module)
    {
        Assert.ThrowsExactly<ArgumentException>(() => CrudArtifactGenerator.GenerateVueClient(
            FullNetCrudSchemaTests.CreateProductSchema(), module));
        Assert.ThrowsExactly<ArgumentException>(() => ModuleClientRouteTarget.Create(
            "/catalog/products", "catalog-products", "ui/views/Products.vue", clientModuleSpecifier: module));
    }
}
