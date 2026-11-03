using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Identity.Features.ManageHostMenus;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>旧 AI 分组修复必须在双库实际生效，导航不授予模型配置或 FAQ 正文权限。</summary>
[TestClass] [DoNotParallelize]
public sealed class AiFaqNavigationApiTests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public async Task Faq_reviewer_can_enter_repaired_ai_directory_without_model_permissions(DatabaseProvider provider)
    {
        var mysql = provider == DatabaseProvider.MySql;
        var connection = mysql ? await SharedDatabaseFixture.CreateMySqlDatabaseAsync() : await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(!mysql, connection);
        await db.OpenAsync();
        if (Environment.GetEnvironmentVariable("FULLNET_K04_LIFECYCLE_DATABASE_REGISTRY") is { Length: > 0 } registry)
            await File.AppendAllTextAsync(registry, JsonSerializer.Serialize(new { provider = provider.ToString(), database = db.Database, purpose = "faq-navigation" }) + "\n");
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync();
        Assert.AreEqual(1, await db.ExecuteAsync("UPDATE fn_identity_navigation SET RequiredPermission=@Permission WHERE RouteName='module-ai' AND ScopeKey='host' AND TenantId IS NULL", new { Permission = AiModelPermissions.Read }));
        var before = await db.QuerySingleAsync<int>("SELECT Version FROM fn_identity_navigation WHERE RouteName='module-ai' AND ScopeKey='host' AND TenantId IS NULL");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var tenant = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>(); tenant.SetHost();
            try
            {
                var sync = scope.ServiceProvider.GetRequiredService<HostNavigationCatalogSyncService>();
                await sync.SyncMissingCatalogEntriesAsync(); await sync.SyncMissingCatalogEntriesAsync();
            }
            finally { tenant.Clear(); }
        }
        Assert.AreEqual(IdentityAuthorizationContributor.NavigationRead, await db.QuerySingleAsync<string>("SELECT RequiredPermission FROM fn_identity_navigation WHERE RouteName='module-ai' AND ScopeKey='host' AND TenantId IS NULL"));
        Assert.AreEqual(before + 1, await db.QuerySingleAsync<int>("SELECT Version FROM fn_identity_navigation WHERE RouteName='module-ai' AND ScopeKey='host' AND TenantId IS NULL"));
        using var client = factory.CreateClientForHost("localhost");
        foreach (var allowed in new[] { true, false })
        {
            var permissions = allowed ? new[] { IdentityAuthorizationContributor.NavigationRead, "platform.dashboard.read", AiKnowledgeFaqPermissions.Read }
                : new[] { IdentityAuthorizationContributor.NavigationRead, "platform.dashboard.read" };
            var actor = await factory.CreateHostIdentityAsync("faq-navigation-" + Guid.CreateVersion7().ToString("N"), permissions);
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/navigation"); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", actor.AccessToken);
            using var response = await client.SendAsync(request); Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(); Assert.AreEqual(allowed, body.Contains("ai-knowledge-faqs", StringComparison.Ordinal));
            Assert.IsFalse(body.Contains("ai-model-configs", StringComparison.Ordinal));
        }
    }
}
