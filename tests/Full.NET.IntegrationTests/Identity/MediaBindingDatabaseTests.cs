using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.IntegrationTests.Document;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Tenancy;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.TenantBranding;
using Full.NET.Modules.Tenancy.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Dapper;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>真实双库 HTTP 行锁竞争，覆盖头像、签名与 Host Logo 的上传和删除。</summary>
[TestClass]
[DoNotParallelize]
public sealed class MediaBindingDatabaseTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Media_bindings_preserve_concurrent_winners(DatabaseProvider provider)
    {
        var cs = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, cs); await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("localhost");
        var token = await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, "admin");
        var tenant = await MediaBindingHttpAssertions.VerifyAsync(client, provider, cs, token, CancellationToken.None);
        await using var db = DocumentReleaseTestConnections.Create(provider, cs); await db.OpenAsync();
        await using var scope = factory.Services.CreateAsyncScope(); var services = scope.ServiceProvider;
        var writer = services.GetRequiredService<ICurrentTenantContextWriter>();
        writer.SetTenant(new TenantContext(tenant, "media-proof", "媒体验收"));
        try
        {
            // 权威查询返回后由另一个真实连接递增版本，验证 TenantRequired 变体也保留原 Logo。
            var query = new CurrentLogoSnapshot(services.GetRequiredService<IQueryExecutor>(), async () =>
                await db.ExecuteAsync("UPDATE fn_tenancy_tenant SET Version = Version + 1 WHERE Id = @Id", new { Id = tenant }));
            var service = new TenantBrandingMediaService(query, services.GetRequiredService<ICommandExecutor>(),
                services.GetRequiredService<ICommandTransaction>(), services.GetRequiredService<IClock>(), writer,
                services.GetRequiredService<TenantBrandingService>(), services.GetRequiredService<TenantCacheInvalidator>(),
                hostFileReferenceClaimService: services.GetRequiredService<IHostFileReferenceClaimService>());
            var file = await db.QuerySingleAsync<Guid>("SELECT LogoFileId FROM fn_tenancy_tenant WHERE Id = @Id", new { Id = tenant });
            var result = await service.DeleteLogoCurrentAsync(tenant);
            Assert.IsFalse(result.IsSuccess); Assert.AreEqual(TenancyErrorCodes.VersionConflict, result.Error!.Code);
            Assert.AreEqual(file, await db.QuerySingleAsync<Guid>("SELECT LogoFileId FROM fn_tenancy_tenant WHERE Id = @Id", new { Id = tenant }));
            Assert.AreEqual("active", await db.QuerySingleAsync<string>("SELECT State FROM fn_files_file_reference_claim WHERE IdempotencyKey = @Key",
                new { Key = HostFileReferenceClaimIdempotencyKeys.TenancyTenantLogo(tenant, file) }));
        }
        finally { writer.Clear(); }
    }

    private sealed class CurrentLogoSnapshot(IQueryExecutor inner, Func<Task> changed) : IQueryExecutor
    {
        private bool _used;
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
            => inner.QueryAsync<T>(statement, parameters, cancellationToken);
        public async Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            var result = await inner.QuerySingleOrDefaultAsync<T>(statement, parameters, cancellationToken);
            if (!_used && statement == TenantSql.FindTenantBrandingCurrent) { _used = true; await changed(); }
            return result;
        }
    }
}
