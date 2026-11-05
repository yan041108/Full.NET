using System.Net;
using System.Net.Http.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Files;

/// <summary>正式暂停/恢复与真实小文件读取共同验证资源存储层，不依赖调用模块重复补守卫。</summary>
[TestClass]
public sealed class TenantResourceFileActivityTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Ready_content_is_blocked_during_suspension_and_available_after_reactivation(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync();
        using var host = factory.CreateClientForHost("localhost");
        host.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(host, "admin"));
        var page = (await host.GetFromJsonAsync<PagedResult<TenantSummary>>("/api/v1/tenancy/tenants?pageSize=100"))!;
        var original = page.Items.Single(item => item.Identifier == "acme");
        await using (var setup = factory.Services.CreateAsyncScope())
        {
            var context = setup.ServiceProvider.GetRequiredService<CurrentTenantAccessor>(); context.SetHost();
            try
            {
                var identifier = "file-read-" + Guid.NewGuid().ToString("N")[..10];
                var spare = await setup.ServiceProvider.GetRequiredService<ITenantProvisioningService>().ProvisionAsync(
                    new ProvisionTenantRequest(identifier, "File read spare", identifier + ".localhost"));
                Assert.IsTrue(spare.IsSuccess, spare.Error?.Code);
            }
            finally { context.Clear(); }
        }
        var resourceId = Guid.CreateVersion7();
        using var bytes = new MemoryStream(new byte[] { 1, 2, 3 });
        var uploaded = await InOriginalTenantAsync(store => store.UploadAsync("import_export", resourceId, Guid.CreateVersion7(),
            "activity.bin", "application/octet-stream", bytes, 3));
        Assert.IsTrue(uploaded.IsSuccess, uploaded.Error?.Code);
        var fileId = uploaded.Value!.FileId;
        await AssertReadableAsync();
        await AssertDescriptorAsync(true);
        using var paused = await host.PostAsJsonAsync($"/api/v1/tenancy/tenants/{original.Id:D}/suspend", new SuspendTenantRequest(original.Version));
        Assert.AreEqual(HttpStatusCode.OK, paused.StatusCode, await paused.Content.ReadAsStringAsync());
        var suspended = (await paused.Content.ReadFromJsonAsync<TenantSummary>())!;
        var denied = await InOriginalTenantAsync(store => store.OpenReadyContentAsync("import_export", resourceId, fileId));
        Assert.IsFalse(denied.IsSuccess); Assert.AreEqual(FilesErrorCodes.TenantInactive, denied.Error!.Code);
        await AssertDescriptorAsync(false);
        using var resumed = await host.PostAsJsonAsync($"/api/v1/tenancy/tenants/{original.Id:D}/reactivate", new ReactivateTenantRequest(suspended.Version));
        Assert.AreEqual(HttpStatusCode.OK, resumed.StatusCode, await resumed.Content.ReadAsStringAsync());
        await AssertReadableAsync();
        await AssertDescriptorAsync(true);

        async Task AssertDescriptorAsync(bool expected)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
            context.SetTenant(new TenantContext(original.Id, original.Identifier, original.Name));
            try
            {
                var descriptor = await scope.ServiceProvider.GetRequiredService<ITenantResourceFileDescriptorReader>()
                    .GetReadyDescriptorAsync("import_export", resourceId, fileId);
                Assert.AreEqual(expected, descriptor is not null);
                if (descriptor is not null)
                {
                    Assert.AreEqual(fileId, descriptor.FileId); Assert.AreEqual(3L, descriptor.SizeBytes);
                    Assert.AreEqual("activity.bin", descriptor.OriginalFileName); Assert.AreEqual(uploaded.Value.ContentHash, descriptor.ContentHash);
                }
            }
            finally { context.Clear(); }
        }

        async Task<T> InOriginalTenantAsync<T>(Func<ITenantResourceFileStore, Task<T>> action)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
            context.SetTenant(new TenantContext(original.Id, original.Identifier, original.Name));
            try { return await action(scope.ServiceProvider.GetRequiredService<ITenantResourceFileStore>()); }
            finally { context.Clear(); }
        }
        async Task AssertReadableAsync()
        {
            var result = await InOriginalTenantAsync(store => store.OpenReadyContentAsync("import_export", resourceId, fileId));
            Assert.IsTrue(result.IsSuccess, result.Error?.Code);
            await using var stream = result.Value!.Content; using var copy = new MemoryStream();
            await stream.CopyToAsync(copy); CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, copy.ToArray());
        }
    }
}
