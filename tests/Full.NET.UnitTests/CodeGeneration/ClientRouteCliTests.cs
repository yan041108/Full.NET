using System.Security.Cryptography;
using System.Text;
using Full.NET.CodeGeneration.Cli;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class ClientRouteCliTests
{
    [TestMethod]
    public async Task Vue_only_route_reports_missing_prerequisites_without_writing()
    {
        using var fixture = new RouteFixture();
        var result = await fixture.ApplyAsync();
        Assert.AreEqual(2, result.Code);
        StringAssert.Contains(result.Error, "不存在");
        Assert.AreEqual(0, Directory.GetFiles(fixture.Repository, "*", SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task Vue_only_route_requires_owned_registry_without_writing()
    {
        using var fixture = new RouteFixture();
        fixture.WritePrerequisites(ownedRegistry: false);
        var before = fixture.Capture();
        var result = await fixture.ApplyAsync();
        Assert.AreEqual(2, result.Code);
        StringAssert.Contains(result.Error, "生成清单拥有");
        CollectionAssert.AreEquivalent(before, fixture.Capture());
    }

    [TestMethod]
    public async Task Vue_only_route_updates_router_and_second_apply_is_idempotent()
    {
        using var fixture = new RouteFixture();
        fixture.WritePrerequisites(ownedRegistry: true);
        var first = await fixture.ApplyAsync();
        Assert.AreEqual(0, first.Code, first.Error);
        StringAssert.Contains(first.Output, "Update ui/admin/src/router/index.ts");
        Assert.IsFalse(first.Output.Contains("layui", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(File.ReadAllText(Path.Combine(fixture.Repository, "ui/admin/src/router/index.ts")), "name: 'catalog-products'");
        var after = fixture.Capture();
        var second = await fixture.ApplyAsync();
        Assert.AreEqual(0, second.Code, second.Error);
        StringAssert.Contains(second.Output, "Unchanged ui/admin/src/router/index.ts");
        CollectionAssert.AreEquivalent(after, fixture.Capture());
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.Repository, "ui/admin-layui")));
    }

    private const string ValidSchemaJson = """
        {
          "ownerKey": "acme",
          "moduleKey": "catalog",
          "entityKey": "product",
          "databaseTableName": "acme_catalog_product",
          "rootNamespace": "Acme.Modules.Catalog",
          "clrTypeName": "Product",
          "apiResourceName": "products",
          "permissionResourceName": "products",
          "isTenantScoped": true,
          "hasVersion": true,
          "columns": [
            {
              "databaseName": "Id",
              "clrPropertyName": "Id",
              "jsonPropertyName": "id",
              "scalarType": "Uuid"
            },
            {
              "databaseName": "TenantId",
              "clrPropertyName": "TenantId",
              "jsonPropertyName": "tenantId",
              "scalarType": "Uuid"
            },
            {
              "databaseName": "Name",
              "clrPropertyName": "Name",
              "jsonPropertyName": "displayName",
              "scalarType": "String",
              "maxLength": 200
            },
            {
              "databaseName": "Description",
              "clrPropertyName": "Description",
              "jsonPropertyName": "description",
              "scalarType": "String",
              "isNullable": true,
              "maxLength": 500
            },
            {
              "databaseName": "IsActive",
              "clrPropertyName": "IsActive",
              "jsonPropertyName": "isActive",
              "scalarType": "Boolean"
            },
            {
              "databaseName": "Version",
              "clrPropertyName": "Version",
              "jsonPropertyName": "version",
              "scalarType": "Int64"
            },
            {
              "databaseName": "CreatedAtUtc",
              "clrPropertyName": "CreatedAtUtc",
              "jsonPropertyName": "createdAtUtc",
              "scalarType": "DateTimeUtc"
            }
          ]
        }
        """;

    private sealed class RouteFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"fullnet-route-cli-{Guid.NewGuid():N}");
        private readonly string _schema;
        private readonly string _target;
        public string Repository { get; }

        public RouteFixture()
        {
            Repository = Path.Combine(_root, "repository");
            Directory.CreateDirectory(Repository);
            _schema = Path.Combine(_root, "schema.json");
            _target = Path.Combine(_root, "target.json");
            File.WriteAllText(_schema, ValidSchemaJson, new UTF8Encoding(false));
            File.WriteAllText(_target, """
                {
                  "moduleName":"Catalog",
                  "moduleProjectPath":"src/Modules/Acme.Modules.Catalog/Acme.Modules.Catalog.csproj",
                  "moduleEntryPointPath":"src/Modules/Acme.Modules.Catalog/CatalogModule.cs",
                  "compositionProjectPath":"src/Composition/Acme.Composition/Acme.Composition.csproj",
                  "compositionCatalogPath":"src/Composition/Acme.Composition/ModuleCatalog.cs",
                  "vueRouterPath":"ui/admin/src/router/index.ts",
                  "clientRoute":{"routePath":"/catalog/products","vueRouteName":"catalog-products","vueComponentPath":"ui/admin/src/views/CatalogProductsView.vue"}
                }
                """, new UTF8Encoding(false));
        }

        public void WritePrerequisites(bool ownedRegistry)
        {
            Write("src/Modules/Acme.Modules.Catalog/Acme.Modules.Catalog.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            Write("src/Modules/Acme.Modules.Catalog/CatalogModule.cs", """
                using Acme.Modules.Catalog.Generated;
                namespace Acme.Modules.Catalog;
                public sealed class CatalogModule {
                  public void AddServices(IServiceCollection services, IConfiguration configuration) {
                    services.AddFullNetGeneratedModuleFeatures();
                  }
                  public void MapEndpoints(IEndpointRouteBuilder endpoints) {
                    endpoints.MapFullNetGeneratedModuleFeatures();
                  }
                }
                """);
            Write("src/Composition/Acme.Composition/Acme.Composition.csproj", """
                <Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="../../Modules/Acme.Modules.Catalog/Acme.Modules.Catalog.csproj" /></ItemGroup></Project>
                """);
            Write("src/Composition/Acme.Composition/ModuleCatalog.cs", """
                using Acme.Modules.Catalog;
                namespace Acme.Composition;
                public static class ModuleCatalog {
                  private static IReadOnlyList<IFullNetModule> CreateModules() =>
                  [
                    new CatalogModule(),
                  ];
                }
                """);
            Write("ui/admin/src/router/index.ts", """
                export function createAppRouter() {
                  return createRouter({
                    routes: [
                      { path: '/403', component: loadStatusView }
                    ]
                  });
                }
                """);
            Write("ui/admin/src/views/CatalogProductsView.vue", "<template><div>Catalog</div></template>");
            if (ownedRegistry)
            {
                const string registry = "// 测试聚合桥所有权\n";
                Write("src/Modules/Acme.Modules.Catalog/Generated/FullNetGeneratedModuleFeatures.g.cs", registry);
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(registry))).ToLowerInvariant();
                Write("src/Modules/Acme.Modules.Catalog/.fullnet/codegeneration-manifest.json",
                    $$"""{"schemaVersion":1,"artifacts":[{"relativePath":"Generated/FullNetGeneratedModuleFeatures.g.cs","sha256":"{{hash}}"}]}""");
            }
        }

        private void Write(string relativePath, string content)
        {
            var path = Path.Combine(Repository, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        public string[] Capture() => Directory.GetFiles(Repository, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(Repository, path) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))
            .ToArray();

        public async Task<(int Code, string Output, string Error)> ApplyAsync()
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            var code = await CodeGenerationCli.RunAsync(
                ["apply-client-route-integration", "--schema", _schema, "--repository", Repository, "--target", _target], output, error);
            return (code, output.ToString(), error.ToString());
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
