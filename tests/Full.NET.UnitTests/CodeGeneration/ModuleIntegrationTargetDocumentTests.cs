using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Full.NET.CodeGeneration.Cli;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class ModuleIntegrationTargetDocumentTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Vue_only_target_does_not_require_frozen_layui_fields(bool includeRoute)
    {
        using var fixture = new TargetFixture();
        if (includeRoute)
        {
            fixture.Document["clientRoute"] = JsonNode.Parse(
                """{"routePath":"/catalog-products","vueRouteName":"catalog-products","vueComponentPath":"ui/admin/src/views/CatalogProductsView.vue"}""");
        }
        var target = await fixture.LoadAsync();

        Assert.IsNull(target.LayuiRouterPath);
        Assert.IsNull(target.ClientRoute?.LayuiControllerPath);
        Assert.AreEqual(includeRoute, target.ClientRoute is not null);
    }

    [TestMethod]
    [DataRow("layuiControllerPath", "ui/admin-layui/js/controllers/catalog-products.js")]
    [DataRow("layuiControllerExport", "createCatalogProductsController")]
    public async Task Explicit_layui_route_requires_both_controller_fields(string property, string value)
    {
        using var fixture = new TargetFixture();
        fixture.Document["clientRoute"] = JsonNode.Parse(
            """{"routePath":"/catalog-products","vueRouteName":"catalog-products","vueComponentPath":"ui/admin/src/views/CatalogProductsView.vue"}""");
        fixture.Document["clientRoute"]![property] = value;

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.LoadAsync());
    }

    [TestMethod]
    public async Task Unknown_target_field_is_still_rejected()
    {
        using var fixture = new TargetFixture();
        fixture.Document["layuiRouterPath"] = "ui/admin-layui/js/core/route-controllers.js";
        fixture.Document["unexpectedField"] = "probe";

        await Assert.ThrowsExactlyAsync<JsonException>(() => fixture.LoadAsync());
    }

    private sealed class TargetFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"fullnet-target-{Guid.NewGuid():N}");
        public JsonObject Document { get; } = JsonNode.Parse(
            """
            {
              "moduleName":"Catalog",
              "moduleProjectPath":"src/Modules/Acme.Modules.Catalog/Acme.Modules.Catalog.csproj",
              "moduleEntryPointPath":"src/Modules/Acme.Modules.Catalog/CatalogModule.cs",
              "compositionProjectPath":"src/Composition/Acme.Composition/Acme.Composition.csproj",
              "compositionCatalogPath":"src/Composition/Acme.Composition/ModuleCatalog.cs",
              "vueRouterPath":"ui/admin/src/router/index.ts"
            }
            """)!.AsObject();

        public TargetFixture() => Directory.CreateDirectory(_root);

        public async Task<Full.NET.Data.CodeGeneration.Integration.ModuleIntegrationTarget> LoadAsync()
        {
            var path = Path.Combine(_root, "integration-target.json");
            File.WriteAllText(path, Document.ToJsonString(), new UTF8Encoding(false));
            return await ModuleIntegrationTargetDocument.LoadAsync(path, CancellationToken.None);
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
