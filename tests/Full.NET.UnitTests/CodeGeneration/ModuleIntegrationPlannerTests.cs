using Full.NET.Data.CodeGeneration.Integration;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class ModuleIntegrationPlannerTests
{
    [TestMethod]
    public void Explicit_client_route_target_preserves_trusted_local_mapping()
    {
        var clientRoute = ModuleClientRouteTarget.Create(
            routePath: "/catalog/products",
            vueRouteName: "catalog-products",
            vueComponentPath:
                "ui/admin/src/views/CatalogProductsView.vue",
            layuiControllerPath:
                "ui/admin-layui/js/core/catalog-products.js",
            layuiControllerExport:
                "createCatalogProductsController");

        var target = ModuleIntegrationTarget.Create(
            moduleName: "Catalog",
            moduleProjectPath:
                "src/Modules/Acme.Modules.Catalog/Acme.Modules.Catalog.csproj",
            moduleEntryPointPath:
                "src/Modules/Acme.Modules.Catalog/CatalogModule.cs",
            compositionProjectPath:
                "src/Composition/Acme.Composition/Acme.Composition.csproj",
            compositionCatalogPath:
                "src/Composition/Acme.Composition/ModuleCatalog.cs",
            vueRouterPath:
                "ui/admin/src/router/index.ts",
            layuiRouterPath:
                "ui/admin-layui/js/core/route-controllers.js",
            clientRoute);

        Assert.AreSame(clientRoute, target.ClientRoute);
        Assert.AreEqual(
            "/catalog/products",
            target.ClientRoute!.RoutePath);
    }

    [TestMethod]
    public void Client_route_target_rejects_unstable_codes_and_paths()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            ModuleClientRouteTarget.Create(
                routePath: "/Catalog/products",
                vueRouteName: "catalog-products",
                vueComponentPath:
                    "ui/admin/src/views/CatalogProductsView.vue",
                layuiControllerPath:
                    "ui/admin-layui/js/core/catalog-products.js",
                layuiControllerExport:
                    "createCatalogProductsController"));
        Assert.ThrowsExactly<ArgumentException>(() =>
            ModuleClientRouteTarget.Create(
                routePath: "/catalog/products",
                vueRouteName: "catalog.products",
                vueComponentPath:
                    "../outside.vue",
                layuiControllerPath:
                    "ui/admin-layui/js/core/catalog-products.js",
                layuiControllerExport:
                    "createCatalogProductsController"));
        Assert.ThrowsExactly<ArgumentException>(() =>
            ModuleClientRouteTarget.Create(
                routePath: "/catalog/products",
                vueRouteName: "catalog-products",
                vueComponentPath:
                    "ui/admin/src/views/CatalogProductsView.vue",
                layuiControllerPath:
                    "ui/admin-layui/js/core/catalog-products.js",
                layuiControllerExport:
                    "catalogProducts"));
    }

    [TestMethod]
    public void Existing_module_reports_exact_changes_without_mutating_snapshot()
    {
        var target = CreateTarget();
        var files = CreateExistingFiles(target);
        var original = files.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);

        var plan = ModuleIntegrationPlanner.Plan(
            FullNetCrudSchemaTests.CreateProductSchema(),
            target,
            new ModuleIntegrationSnapshot(files));

        CollectionAssert.AreEqual(
            new[]
            {
                ModuleIntegrationArea.BackendArtifacts,
                ModuleIntegrationArea.ModuleProject,
                ModuleIntegrationArea.ModuleServices,
                ModuleIntegrationArea.ModuleEndpoints,
                ModuleIntegrationArea.CompositionProject,
                ModuleIntegrationArea.CompositionCatalog,
                ModuleIntegrationArea.VueRoute,
                ModuleIntegrationArea.LayuiRoute,
            },
            plan.Items.Select(item => item.Area).ToArray());
        Assert.AreEqual(
            ModuleIntegrationStatus.ChangeRequired,
            plan.Items[0].Status);
        Assert.AreEqual(
            "src/Modules/Acme.Modules.Catalog/Generated",
            plan.Items[0].RelativePath);
        Assert.AreEqual(
            ModuleIntegrationStatus.Satisfied,
            plan.Items[1].Status);
        Assert.AreEqual(
            ModuleIntegrationStatus.ChangeRequired,
            plan.Items[2].Status);
        StringAssert.Contains(
            plan.Items[2].Instruction,
            "AddFullNetGeneratedModuleFeatures");
        Assert.AreEqual(
            ModuleIntegrationStatus.ChangeRequired,
            plan.Items[3].Status);
        StringAssert.Contains(
            plan.Items[3].Instruction,
            "MapFullNetGeneratedModuleFeatures");
        Assert.IsTrue(plan.Items
            .Skip(4)
            .Take(2)
            .All(item =>
                item.Status == ModuleIntegrationStatus.Satisfied));
        Assert.IsTrue(plan.Items
            .Skip(6)
            .All(item =>
                item.Status == ModuleIntegrationStatus.ManualReview));
        CollectionAssert.AreEquivalent(
            original.ToArray(),
            files.ToArray());
    }

    [TestMethod]
    public void Already_registered_feature_is_satisfied_but_routes_remain_manual()
    {
        var target = CreateTarget();
        var files = CreateExistingFiles(target);
        files[target.ModuleEntryPointPath] =
            """
            namespace Acme.Modules.Catalog;

            public sealed class CatalogModule
            {
                public void AddServices(IServiceCollection services)
                {
                    services.AddFullNetGeneratedModuleFeatures();
                }

                public void MapEndpoints(IEndpointRouteBuilder endpoints)
                {
                    endpoints.MapFullNetGeneratedModuleFeatures();
                }
            }
            """;

        var plan = ModuleIntegrationPlanner.Plan(
            FullNetCrudSchemaTests.CreateProductSchema(),
            target,
            new ModuleIntegrationSnapshot(files));

        Assert.AreEqual(
            ModuleIntegrationStatus.Satisfied,
            plan.Items.Single(item =>
                item.Area == ModuleIntegrationArea.ModuleServices).Status);
        Assert.AreEqual(
            ModuleIntegrationStatus.Satisfied,
            plan.Items.Single(item =>
                item.Area == ModuleIntegrationArea.ModuleEndpoints).Status);
        Assert.AreEqual(
            ModuleIntegrationStatus.ManualReview,
            plan.Items.Single(item =>
                item.Area == ModuleIntegrationArea.VueRoute).Status);
        Assert.AreEqual(
            ModuleIntegrationStatus.ManualReview,
            plan.Items.Single(item =>
                item.Area == ModuleIntegrationArea.LayuiRoute).Status);
    }

    [TestMethod]
    public void Explicit_client_routes_report_change_then_satisfied()
    {
        var route = ModuleClientRouteTarget.Create(
            routePath: "/catalog/products",
            vueRouteName: "catalog-products",
            vueComponentPath:
                "ui/admin/src/views/CatalogProductsView.vue",
            layuiControllerPath:
                "ui/admin-layui/js/core/catalog-products.js",
            layuiControllerExport:
                "createCatalogProductsController");
        var target = ModuleIntegrationTarget.Create(
            moduleName: "Catalog",
            moduleProjectPath:
                "src/Modules/Acme.Modules.Catalog/Acme.Modules.Catalog.csproj",
            moduleEntryPointPath:
                "src/Modules/Acme.Modules.Catalog/CatalogModule.cs",
            compositionProjectPath:
                "src/Composition/Acme.Composition/Acme.Composition.csproj",
            compositionCatalogPath:
                "src/Composition/Acme.Composition/ModuleCatalog.cs",
            vueRouterPath:
                "ui/admin/src/router/index.ts",
            layuiRouterPath:
                "ui/admin-layui/js/core/route-controllers.js",
            route);
        var files = CreateExistingFiles(target);
        files[target.VueRouterPath] =
            """
            export function createAppRouter() {
              return createRouter({
                routes: [
                  { path: '/403', component: loadStatusView }
                ]
              });
            }
            """;
        files[target.LayuiRouterPath!] =
            """
            export function createLayuiRouteControllerDefinitions(root, options) {
              const sharedOptions = {};
              return new Map([
              ]);
            }
            """;
        files[route.VueComponentPath] =
            "<template><main>Products</main></template>";
        files[route.LayuiControllerPath!] =
            "export function createCatalogProductsController() {}";

        var changePlan = ModuleIntegrationPlanner.Plan(
            FullNetCrudSchemaTests.CreateProductSchema(),
            target,
            new ModuleIntegrationSnapshot(files));
        files[target.VueRouterPath] =
            VueRouteIntegrationEditor.Edit(
                files[target.VueRouterPath],
                target.VueRouterPath,
                route).DesiredContent;
        files[target.LayuiRouterPath!] =
            LayuiRouteIntegrationEditor.Edit(
                files[target.LayuiRouterPath!],
                target.LayuiRouterPath!,
                route).DesiredContent;
        var satisfiedPlan = ModuleIntegrationPlanner.Plan(
            FullNetCrudSchemaTests.CreateProductSchema(),
            target,
            new ModuleIntegrationSnapshot(files));

        Assert.IsTrue(changePlan.Items
            .Where(item => item.Area is
                ModuleIntegrationArea.VueRoute
                or ModuleIntegrationArea.LayuiRoute)
            .All(item =>
                item.Status == ModuleIntegrationStatus.ChangeRequired));
        Assert.IsTrue(satisfiedPlan.Items
            .Where(item => item.Area is
                ModuleIntegrationArea.VueRoute
                or ModuleIntegrationArea.LayuiRoute)
            .All(item =>
                item.Status == ModuleIntegrationStatus.Satisfied));
    }

    [TestMethod]
    public void Missing_module_files_block_backend_and_module_registration()
    {
        var target = CreateTarget();
        var files = CreateExistingFiles(target);
        files.Remove(target.ModuleProjectPath);
        files.Remove(target.ModuleEntryPointPath);

        var plan = ModuleIntegrationPlanner.Plan(
            FullNetCrudSchemaTests.CreateProductSchema(),
            target,
            new ModuleIntegrationSnapshot(files));

        Assert.AreEqual(
            ModuleIntegrationStatus.Blocked,
            plan.Items.Single(item =>
                item.Area == ModuleIntegrationArea.BackendArtifacts).Status);
        Assert.IsTrue(plan.Items
            .Where(item => item.Area is
                ModuleIntegrationArea.ModuleProject
                or ModuleIntegrationArea.ModuleServices
                or ModuleIntegrationArea.ModuleEndpoints)
            .All(item => item.Status == ModuleIntegrationStatus.Blocked));
        Assert.AreEqual(
            ModuleIntegrationStatus.Blocked,
            plan.Items.Single(item =>
                item.Area == ModuleIntegrationArea.CompositionProject).Status);
        Assert.AreEqual(
            ModuleIntegrationStatus.Blocked,
            plan.Items.Single(item =>
                item.Area == ModuleIntegrationArea.CompositionCatalog).Status);
    }

    [TestMethod]
    public void Comments_do_not_satisfy_registration_and_invalid_project_blocks()
    {
        var target = CreateTarget();
        var files = CreateExistingFiles(target);
        files[target.ModuleEntryPointPath] =
            """
            namespace Acme.Modules.Catalog;

            public sealed class CatalogModule
            {
                // services.AddFullNetGeneratedModuleFeatures();
                /* endpoints.MapFullNetGeneratedModuleFeatures(); */
            }
            """;
        files[target.CompositionProjectPath] = "<Project>";

        var plan = ModuleIntegrationPlanner.Plan(
            FullNetCrudSchemaTests.CreateProductSchema(),
            target,
            new ModuleIntegrationSnapshot(files));

        Assert.AreEqual(
            ModuleIntegrationStatus.ChangeRequired,
            plan.Items.Single(item =>
                item.Area == ModuleIntegrationArea.ModuleServices).Status);
        Assert.AreEqual(
            ModuleIntegrationStatus.ChangeRequired,
            plan.Items.Single(item =>
                item.Area == ModuleIntegrationArea.ModuleEndpoints).Status);
        Assert.AreEqual(
            ModuleIntegrationStatus.Blocked,
            plan.Items.Single(item =>
                item.Area == ModuleIntegrationArea.CompositionProject).Status);
    }

    private static ModuleIntegrationTarget CreateTarget() =>
        ModuleIntegrationTarget.Create(
            moduleName: "Catalog",
            moduleProjectPath:
                "src/Modules/Acme.Modules.Catalog/Acme.Modules.Catalog.csproj",
            moduleEntryPointPath:
                "src/Modules/Acme.Modules.Catalog/CatalogModule.cs",
            compositionProjectPath:
                "src/Composition/Acme.Composition/Acme.Composition.csproj",
            compositionCatalogPath:
                "src/Composition/Acme.Composition/ModuleCatalog.cs",
            vueRouterPath:
                "ui/admin/src/router/index.ts",
            layuiRouterPath:
                "ui/admin-layui/js/core/route-controllers.js");

    private static Dictionary<string, string> CreateExistingFiles(
        ModuleIntegrationTarget target) =>
        new(StringComparer.Ordinal)
        {
            [target.ModuleProjectPath] =
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <FrameworkReference Include="Microsoft.AspNetCore.App" />
                  </ItemGroup>
                </Project>
                """,
            [target.ModuleEntryPointPath] =
                """
                namespace Acme.Modules.Catalog;

                public sealed class CatalogModule
                {
                }
                """,
            [target.CompositionProjectPath] =
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <ProjectReference Include="..\..\Modules\Acme.Modules.Catalog\Acme.Modules.Catalog.csproj" />
                  </ItemGroup>
                </Project>
                """,
            [target.CompositionCatalogPath] =
                """
                using Acme.Modules.Catalog;

                namespace Acme.Composition;

                public static class ModuleCatalog
                {
                    private static object[] CreateModules() =>
                    [
                        new CatalogModule(),
                    ];
                }
                """,
            [target.VueRouterPath] = "export const routes = [];",
            [target.LayuiRouterPath!] = "export const routes = [];",
        };
}
