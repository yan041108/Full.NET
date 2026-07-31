using Full.NET.CodeGeneration.Cli;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class ModuleEntryIntegrationEditorTests
{
    [TestMethod]
    public void Edit_adds_generated_using_and_both_aggregate_calls()
    {
        var result = ModuleEntryIntegrationEditor.Edit(
            """
            using Full.NET.Modularity;
            using Microsoft.AspNetCore.Routing;
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;

            namespace Acme.Modules.Catalog;

            public sealed class CatalogModule : IFullNetModule
            {
                public void AddServices(
                    IServiceCollection services,
                    IConfiguration configuration)
                {
                    services.AddOptions();
                }

                public void MapEndpoints(IEndpointRouteBuilder endpoints)
                {
                    endpoints.MapGet("/catalog", () => "catalog");
                }
            }
            """,
            "Acme.Modules.Catalog");

        Assert.IsTrue(result.Succeeded);
        Assert.IsTrue(result.Changed);
        StringAssert.Contains(
            result.DesiredContent,
            "using Acme.Modules.Catalog.Generated;");
        StringAssert.Contains(
            result.DesiredContent,
            """
                {
                    services.AddFullNetGeneratedModuleFeatures();
                    services.AddOptions();
            """);
        StringAssert.Contains(
            result.DesiredContent,
            """
                {
                    endpoints.MapFullNetGeneratedModuleFeatures();
                    endpoints.MapGet("/catalog", () => "catalog");
            """);
    }

    [TestMethod]
    public void Edit_is_idempotent_when_exact_calls_already_exist()
    {
        const string source =
            """
            using Acme.Modules.Catalog.Generated;
            using Full.NET.Modularity;
            using Microsoft.AspNetCore.Routing;
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;

            namespace Acme.Modules.Catalog;

            public sealed class CatalogModule : IFullNetModule
            {
                public void AddServices(
                    IServiceCollection services,
                    IConfiguration configuration)
                {
                    services.AddFullNetGeneratedModuleFeatures();
                }

                public void MapEndpoints(IEndpointRouteBuilder endpoints)
                {
                    endpoints.MapFullNetGeneratedModuleFeatures();
                }
            }
            """;

        var result = ModuleEntryIntegrationEditor.Edit(
            source,
            "Acme.Modules.Catalog");

        Assert.IsTrue(result.Succeeded);
        Assert.IsFalse(result.Changed);
        Assert.AreEqual(source, result.DesiredContent);
    }

    [TestMethod]
    public void Edit_ignores_calls_inside_comments_and_strings()
    {
        var result = ModuleEntryIntegrationEditor.Edit(
            """
            using Full.NET.Modularity;
            using Microsoft.AspNetCore.Routing;
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;

            namespace Acme.Modules.Catalog;

            public sealed class CatalogModule : IFullNetModule
            {
                public void AddServices(
                    IServiceCollection services,
                    IConfiguration configuration)
                {
                    // services.AddFullNetGeneratedModuleFeatures();
                    var example = "services.AddFullNetGeneratedModuleFeatures();";
                }

                public void MapEndpoints(IEndpointRouteBuilder endpoints)
                {
                    /* endpoints.MapFullNetGeneratedModuleFeatures(); */
                }
            }
            """,
            "Acme.Modules.Catalog");

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(
            3,
            Count(
                result.DesiredContent,
                "services.AddFullNetGeneratedModuleFeatures();"));
        Assert.AreEqual(
            2,
            Count(
                result.DesiredContent,
                "endpoints.MapFullNetGeneratedModuleFeatures();"));
    }

    [TestMethod]
    public void Edit_rejects_expression_bodied_entry_method()
    {
        var result = ModuleEntryIntegrationEditor.Edit(
            """
            using Full.NET.Modularity;
            using Microsoft.AspNetCore.Routing;
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;

            namespace Acme.Modules.Catalog;

            public sealed class CatalogModule : IFullNetModule
            {
                public void AddServices(
                    IServiceCollection services,
                    IConfiguration configuration)
                {
                }

                public void MapEndpoints(IEndpointRouteBuilder endpoints) =>
                    endpoints.MapGet("/catalog", () => "catalog");
            }
            """,
            "Acme.Modules.Catalog");

        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(result.Changed);
        CollectionAssert.Contains(
            result.Diagnostics.ToArray(),
            "MapEndpoints 必须使用可验证的块体方法。");
    }

    [TestMethod]
    public void Edit_rejects_duplicate_entry_methods()
    {
        var result = ModuleEntryIntegrationEditor.Edit(
            """
            using Full.NET.Modularity;
            using Microsoft.AspNetCore.Routing;
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;

            namespace Acme.Modules.Catalog;

            public sealed class CatalogModule : IFullNetModule
            {
                public void AddServices(
                    IServiceCollection services,
                    IConfiguration configuration)
                {
                }

                public void AddServices(IServiceCollection services)
                {
                }

                public void MapEndpoints(IEndpointRouteBuilder endpoints)
                {
                }
            }
            """,
            "Acme.Modules.Catalog");

        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(result.Changed);
        CollectionAssert.Contains(
            result.Diagnostics.ToArray(),
            "AddServices 必须且只能存在一个可验证声明。");
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(
                   value,
                   index,
                   StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
