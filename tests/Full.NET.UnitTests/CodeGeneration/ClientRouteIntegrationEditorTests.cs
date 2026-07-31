using Full.NET.Data.CodeGeneration.Integration;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class ClientRouteIntegrationEditorTests
{
    private static readonly ModuleClientRouteTarget Route =
        ModuleClientRouteTarget.Create(
            routePath: "/catalog/products",
            vueRouteName: "catalog-products",
            vueComponentPath:
                "ui/admin/src/views/CatalogProductsView.vue",
            layuiControllerPath:
                "ui/admin-layui/js/core/catalog-products.js",
            layuiControllerExport:
                "createCatalogProductsController");

    [TestMethod]
    public void Vue_editor_inserts_before_status_routes_and_is_idempotent()
    {
        const string source =
            """
            export function createAppRouter() {
              return createRouter({
                routes: [
                  { name: 'overview', path: '/', component: OverviewView },
                  { path: '/403', component: loadStatusView },
                  { path: '/:pathMatch(.*)*', redirect: '/404' }
                ]
              });
            }

            """;

        var first = VueRouteIntegrationEditor.Edit(
            source,
            "ui/admin/src/router/index.ts",
            Route);
        var second = VueRouteIntegrationEditor.Edit(
            first.DesiredContent,
            "ui/admin/src/router/index.ts",
            Route);

        Assert.IsTrue(first.Succeeded);
        Assert.IsTrue(first.Changed);
        StringAssert.Contains(
            first.DesiredContent,
            "name: 'catalog-products'");
        StringAssert.Contains(
            first.DesiredContent,
            "path: '/catalog/products'");
        StringAssert.Contains(
            first.DesiredContent,
            "import('../views/CatalogProductsView.vue')");
        Assert.IsTrue(first.DesiredContent.IndexOf(
                "name: 'catalog-products'",
                StringComparison.Ordinal)
            < first.DesiredContent.IndexOf(
                "{ path: '/403'",
                StringComparison.Ordinal));
        Assert.IsTrue(second.Succeeded);
        Assert.IsFalse(second.Changed);
    }

    [TestMethod]
    public void Vue_editor_ignores_comment_decoys_but_rejects_conflicts()
    {
        const string decoy =
            """
            // path: '/catalog/products',
            export function createAppRouter() {
              return createRouter({
                routes: [
                  { path: '/403', component: loadStatusView }
                ]
              });
            }
            """;
        const string conflict =
            """
            export function createAppRouter() {
              return createRouter({
                routes: [
                  {
                    name: 'other-products',
                    path: '/catalog/products',
                    component: () => import('../views/OtherView.vue')
                  },
                  { path: '/403', component: loadStatusView }
                ]
              });
            }
            """;

        var decoyResult = VueRouteIntegrationEditor.Edit(
            decoy,
            "ui/admin/src/router/index.ts",
            Route);
        var conflictResult = VueRouteIntegrationEditor.Edit(
            conflict,
            "ui/admin/src/router/index.ts",
            Route);

        Assert.IsTrue(decoyResult.Succeeded);
        Assert.IsTrue(decoyResult.Changed);
        Assert.IsFalse(conflictResult.Succeeded);
        Assert.IsFalse(conflictResult.Changed);
    }

    [TestMethod]
    public void Vue_editor_does_not_combine_fields_from_adjacent_routes()
    {
        const string source =
            """
            export function createAppRouter() {
              return createRouter({
                routes: [
                  { name: 'catalog-products', path: '/other', component: () => import('../views/CatalogProductsView.vue') },
                  { name: 'other-products', path: '/catalog/products', component: () => import('../views/OtherView.vue') },
                  { path: '/403', component: loadStatusView }
                ]
              });
            }
            """;

        var result = VueRouteIntegrationEditor.Edit(
            source,
            "ui/admin/src/router/index.ts",
            Route);

        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(result.Changed);
    }

    [TestMethod]
    public void Layui_editor_appends_map_entry_and_is_idempotent()
    {
        const string source =
            """
            export function createLayuiRouteControllerDefinitions(root, options) {
              const sharedOptions = {};
              return new Map([
                ['/', defineController(
                  () => import('./overview-dashboard.js'),
                  'createOverviewDashboardController',
                  root,
                  sharedOptions
                )]
              ]);
            }

            """;

        var first = LayuiRouteIntegrationEditor.Edit(
            source,
            "ui/admin-layui/js/core/route-controllers.js",
            Route);
        var second = LayuiRouteIntegrationEditor.Edit(
            first.DesiredContent,
            "ui/admin-layui/js/core/route-controllers.js",
            Route);

        Assert.IsTrue(first.Succeeded);
        Assert.IsTrue(first.Changed);
        StringAssert.Contains(
            first.DesiredContent,
            "['/catalog/products', defineController(");
        StringAssert.Contains(
            first.DesiredContent,
            "() => import('./catalog-products.js')");
        StringAssert.Contains(
            first.DesiredContent,
            "'createCatalogProductsController'");
        Assert.IsTrue(second.Succeeded);
        Assert.IsFalse(second.Changed);
    }

    [TestMethod]
    public void Layui_editor_ignores_comment_decoys_but_rejects_conflicts()
    {
        const string decoy =
            """
            // ['/catalog/products', defineController(
            export function createLayuiRouteControllerDefinitions(root, options) {
              const sharedOptions = {};
              return new Map([
              ]);
            }
            """;
        const string conflict =
            """
            export function createLayuiRouteControllerDefinitions(root, options) {
              const sharedOptions = {};
              return new Map([
                ['/catalog/products', defineController(
                  () => import('./other.js'),
                  'createOtherController',
                  root,
                  sharedOptions
                )]
              ]);
            }
            """;

        var decoyResult = LayuiRouteIntegrationEditor.Edit(
            decoy,
            "ui/admin-layui/js/core/route-controllers.js",
            Route);
        var conflictResult = LayuiRouteIntegrationEditor.Edit(
            conflict,
            "ui/admin-layui/js/core/route-controllers.js",
            Route);

        Assert.IsTrue(decoyResult.Succeeded);
        Assert.IsTrue(decoyResult.Changed);
        Assert.IsFalse(conflictResult.Succeeded);
        Assert.IsFalse(conflictResult.Changed);
    }

    [TestMethod]
    public void Layui_editor_does_not_combine_fields_from_adjacent_entries()
    {
        const string source =
            """
            export function createLayuiRouteControllerDefinitions(root, options) {
              const sharedOptions = {};
              return new Map([
                ['/catalog/products', defineController(
                  () => import('./catalog-products.js'),
                  'createOtherController',
                  root, sharedOptions
                )],
                ['/other', defineController(() => import('./other.js'),
                  'createCatalogProductsController',
                  root,
                  sharedOptions
                )]
              ]);
            }
            """;

        var result = LayuiRouteIntegrationEditor.Edit(
            source,
            "ui/admin-layui/js/core/route-controllers.js",
            Route);

        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(result.Changed);
    }

    [TestMethod]
    public void Editors_reject_nonstandard_shapes()
    {
        var vue = VueRouteIntegrationEditor.Edit(
            "export const routes = [];",
            "ui/admin/src/router/index.ts",
            Route);
        var layui = LayuiRouteIntegrationEditor.Edit(
            "export const routes = [];",
            "ui/admin-layui/js/core/route-controllers.js",
            Route);

        Assert.IsFalse(vue.Succeeded);
        Assert.IsFalse(layui.Succeeded);
    }
}
