using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageHostMenus;
using Full.NET.Modules.Payments.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Payments;

public sealed partial class SubscriptionBillingRecoveryTests
{
    private static async Task AssertPaymentNavigationAsync(IServiceProvider services, DbConnection connection, HttpClient reader)
    {
        var initial = await ReadNavigationAsync();
        AssertPaymentEntries(initial);

        // 旧 Baseline 以商户读取权限控制目录，升级只修原始目录并保留各页面的精确权限。
        Assert.AreEqual(1, await connection.ExecuteAsync("""
            UPDATE fn_identity_navigation SET RequiredPermission='payments.merchants.read'
            WHERE ScopeKey='host' AND TenantId IS NULL AND RouteName='module-payments' AND IsSystem=1
            """));
        Assert.IsFalse((await ReadNavigationAsync()).Any(item => item.RouteName == "payments-orders"));
        var sync = services.GetRequiredService<HostNavigationCatalogSyncService>();
        Assert.AreEqual(1, (await sync.SyncMissingCatalogEntriesAsync()).PermissionUpdated);
        Assert.AreEqual(0, (await sync.SyncMissingCatalogEntriesAsync()).PermissionUpdated);
        AssertPaymentEntries(await ReadNavigationAsync());

        async Task<NavigationNodeResponse[]> ReadNavigationAsync()
        {
            var nodes = await reader.GetFromJsonAsync<NavigationNodeResponse[]>("/api/v1/navigation");
            Assert.IsNotNull(nodes);
            return Flatten(nodes).ToArray();
        }

        static IEnumerable<NavigationNodeResponse> Flatten(IEnumerable<NavigationNodeResponse> nodes) =>
            nodes.SelectMany(node => new[] { node }.Concat(Flatten(node.Children)));

        static void AssertPaymentEntries(IEnumerable<NavigationNodeResponse> nodes)
        {
            var entries = nodes.ToDictionary(node => node.RouteName, StringComparer.Ordinal);
            Assert.AreEqual(PaymentOrderPermissions.Read, entries["payments-orders"].RequiredPermission);
            Assert.AreEqual(PaymentRefundPermissions.Read, entries["payments-refunds"].RequiredPermission);
            Assert.IsFalse(entries.ContainsKey("payments-merchant-configs"));
        }
    }

    internal static Task VerifyBrowserRolesAsync() =>
        VerifyBillingRecoveryAsync(DatabaseProvider.SqlServer, new BillingBrowser());

    private sealed class BillingBrowser
    {
        private readonly string suffix = Guid.CreateVersion7().ToString("N")[..12];
        private readonly string origin = ReserveOrigin();
        private string Reader => "billing-reader-" + suffix;
        private string Operator => "billing-operator-" + suffix;
        private string Reconciler => "billing-reconciler-" + suffix;
        private string SpareTenant => "Billing spare " + suffix;

        internal Dictionary<string, string?> Settings => new()
        {
            ["Identity:AllowedOrigins:0"] = origin,
            ["Identity:AllowedOrigins:1"] = "http://localhost",
            ["Identity:RequireSecureCookies"] = "false",
            ["Tenancy:HostDomains:1"] = "127.0.0.1"
        };

        internal void UseKestrel(FullNetApiFactory factory) => factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));

        internal HttpClient CreateClient(FullNetApiFactory factory, string host)
        {
            var client = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { BaseAddress = new Uri(ApiAddress(factory)) };
            client.DefaultRequestHeaders.Host = host;
            return client;
        }

        internal async Task CreateUsersAsync(FullNetApiFactory factory)
        {
            var basic = new[] { "identity.navigation.read", PaymentOrderPermissions.Read };
            var subscription = new[] { TenancyTenantManagementPermissions.HostTenantsRead, TenancyTenantSubscriptionPermissions.Read };
            await factory.CreateHostIdentityAsync(Reader, [..basic, ..subscription, PaymentRefundPermissions.Read], password: FullNetApiFactory.TestPassword);
            await factory.CreateHostIdentityAsync(Operator, [..basic, ..subscription, TenancyTenantSubscriptionPermissions.Manage, PaymentOrderPermissions.Create],
                password: FullNetApiFactory.TestPassword);
            await factory.CreateHostIdentityAsync(Reconciler, [..basic, PaymentOrderPermissions.Reconcile, PaymentRefundPermissions.Read],
                password: FullNetApiFactory.TestPassword);
            await using var scope = factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
            var identifier = "billing-spare-" + suffix;
            var result = await scope.ServiceProvider.GetRequiredService<ITenantProvisioningService>()
                .ProvisionAsync(new ProvisionTenantRequest(identifier, SpareTenant, identifier + ".localhost"));
            Assert.IsTrue(result.IsSuccess, result.Error?.Code);
        }

        internal async Task RunAsync(FullNetApiFactory factory, string tenantName, Guid subscriptionId, Guid orderId, Guid? refundId, string? phase = null)
        {
            var repository = FullNetApiFactory.FindRepositoryRoot();
            var evidence = Directory.CreateTempSubdirectory("fullnet-billing-browser-");
            var directory = Path.Combine(repository, "tests/e2e/admin-real-stack");
            var start = new ProcessStartInfo("node") { WorkingDirectory = directory, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var argument in new[] { Path.Combine(directory, "node_modules/@playwright/test/cli.js"), "test", "subscription-billing-roles.spec.mjs",
                "--project", "vue-admin", "--workers", "1", "--output", evidence.FullName })
                start.ArgumentList.Add(argument);
            foreach (var key in new[] { "FULLNET_E2E_SKIP_BOOTSTRAP", "FULLNET_E2E_SKIP_VIEWER_PROVISION", "FULLNET_E2E_SKIP_OIDC_FIXTURES" })
                start.Environment[key] = "1";
            start.Environment["FULLNET_E2E_API_URL"] = ApiAddress(factory);
            start.Environment["FULLNET_E2E_ADMIN_ORIGIN"] = origin;
            start.Environment["FULLNET_E2E_PASSWORD"] = FullNetApiFactory.TestPassword;
            start.Environment["FULLNET_E2E_BILLING_ROLES"] = JsonSerializer.Serialize(new
                { tenantName, subscriptionId, orderId, refundId, phase, spareTenant = SpareTenant, reader = Reader, @operator = Operator, reconciler = Reconciler });

            using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动订阅浏览器验收。");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
            try { await process.WaitForExitAsync(timeout.Token); }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
            var output = (await stdout) + (await stderr);
            Assert.AreEqual(0, process.ExitCode, output);
            Assert.IsTrue(output.Contains("1 passed", StringComparison.Ordinal), output);
            evidence.Delete(recursive: true);
        }

        private static string ReserveOrigin()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return $"http://localhost:{((IPEndPoint)listener.LocalEndpoint).Port}";
        }

        private static string ApiAddress(FullNetApiFactory factory) =>
            factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    }
}

/// <summary>用真实后台和独立 Host 会话串联订阅与资金事实。</summary>
[TestClass]
public sealed class SubscriptionBillingBrowserUiTests
{
    [TestMethod]
    [TestCategory("BrowserUi")]
    public Task Scoped_browser_roles_read_frozen_invoice_original_order_and_refund() =>
        SubscriptionBillingRecoveryTests.VerifyBrowserRolesAsync();
}
