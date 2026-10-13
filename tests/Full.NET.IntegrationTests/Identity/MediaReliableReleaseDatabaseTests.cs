using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dapper;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Document;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Reconciliation;
using Full.NET.Modules.Identity.Features.HostFileReferences;
using Full.NET.Modules.Identity.Features.SelfServiceProfile;
using Full.NET.Modules.Tenancy;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.HostFileReferences;
using Full.NET.Modules.Tenancy.Features.TenantBranding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>真实事务核对三类绑定键、发布失败回滚及提交后释放故障的持久化补偿。</summary>
[TestClass]
[DoNotParallelize]
public sealed class MediaReliableReleaseDatabaseTests
{
    private static readonly byte[] Image = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aS1sAAAAASUVORK5CYII=");

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Media_unbind_and_outbox_fact_are_atomic_and_retryable(DatabaseProvider provider)
    {
        var cs = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, cs); await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("localhost");
        var token = await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, "admin");
        await using var db = DocumentReleaseTestConnections.Create(provider, cs); await db.OpenAsync();
        var user = await db.QuerySingleAsync<Guid>("SELECT Id FROM fn_identity_user WHERE Username = 'admin'");
        var slug = "release-" + Guid.CreateVersion7().ToString("N");
        using var create = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tenancy/tenants/") { Content = JsonContent.Create(new ProvisionTenantRequest(slug, "释放验收", slug + ".test")) };
        create.Headers.Authorization = new("Bearer", token);
        using var created = await client.SendAsync(create); Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        var tenant = (await created.Content.ReadFromJsonAsync<TenantSummary>())!.Id;
        foreach (var kind in new[] { "avatar", "signature", "logo" })
        {
            var route = kind == "logo" ? $"/api/v1/tenancy/tenants/{tenant:D}/branding/logo" : "/api/v1/me/profile/" + kind;
            using var upload = new HttpRequestMessage(HttpMethod.Post, route); upload.Headers.Authorization = new("Bearer", token);
            var bytes = new ByteArrayContent(Image); bytes.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            var form = new MultipartFormDataContent(); form.Add(bytes, "file", "media.png"); upload.Content = form;
            using var response = await client.SendAsync(upload); Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
            var table = kind == "logo" ? "fn_tenancy_tenant" : "fn_identity_user_profile";
            var idColumn = kind == "logo" ? "Id" : "UserId"; var subject = kind == "logo" ? tenant : user;
            var prefix = kind == "avatar" ? "Avatar" : kind == "signature" ? "Signature" : "Logo";
            var snapshot = await db.QuerySingleAsync<Binding>($"SELECT {prefix}FileId AS FileId, {prefix}ClaimKey AS ClaimKey, Version FROM {table} WHERE {idColumn} = @Id", new { Id = subject });
            var legacyKey = kind switch {
                "avatar" => HostFileReferenceClaimIdempotencyKeys.IdentityUserAvatar(user, snapshot.FileId),
                "signature" => HostFileReferenceClaimIdempotencyKeys.IdentityUserSignature(user, snapshot.FileId),
                _ => HostFileReferenceClaimIdempotencyKeys.TenancyTenantLogo(tenant, snapshot.FileId),
            };
            Assert.IsTrue(HostFileReferenceClaimIdempotencyKeys.IsBindingKey(legacyKey, snapshot.ClaimKey));
            Assert.AreNotEqual(legacyKey, snapshot.ClaimKey); Assert.IsTrue(snapshot.ClaimKey.Length <= 128);
            var eventType = kind == "logo" ? TenantLogoUnboundOutbox.EventType : IdentityProfileMediaUnboundOutbox.EventType;
            await using var scope = factory.Services.CreateAsyncScope(); var services = scope.ServiceProvider;
            var writer = services.GetRequiredService<ICurrentTenantContextWriter>();
            writer.SetHost();
            try
            {
                var before = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE MessageType = @Type", new { Type = eventType });
                var beforeIds = (await db.QueryAsync<Guid>("SELECT Id FROM fn_outbox_message WHERE MessageType = @Type", new { Type = eventType })).ToHashSet();
                await Assert.ThrowsExactlyAsync<IOException>(() => DeleteAsync(new FailAfterWrite(services.GetRequiredService<IOutboxWriter>())));
                Assert.AreEqual(snapshot.FileId, await db.QuerySingleAsync<Guid>($"SELECT {prefix}FileId FROM {table} WHERE {idColumn} = @Id", new { Id = subject }));
                Assert.AreEqual(snapshot.Version, await db.QuerySingleAsync<int>($"SELECT Version FROM {table} WHERE {idColumn} = @Id", new { Id = subject }));
                Assert.AreEqual(before, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE MessageType = @Type", new { Type = eventType }));
                if (kind == "logo") writer.SetTenant(new TenantContext(tenant, slug, "释放验收"));
                await DeleteAsync(services.GetRequiredService<IOutboxWriter>());
                Assert.IsNull(await db.QuerySingleAsync<Guid?>($"SELECT {prefix}FileId FROM {table} WHERE {idColumn} = @Id", new { Id = subject }));
                Assert.IsNull(await db.QuerySingleAsync<string?>($"SELECT {prefix}ClaimKey FROM {table} WHERE {idColumn} = @Id", new { Id = subject }));
                var fact = (await db.QueryAsync<Fact>("SELECT Id, TenantId, Payload FROM fn_outbox_message WHERE MessageType = @Type", new { Type = eventType })).Single(row => !beforeIds.Contains(row.Id));
                Assert.AreEqual(kind == "logo" ? (Guid?)tenant : null, fact.TenantId);
                Assert.AreEqual("active", await StateAsync(snapshot.ClaimKey));
                var handler = services.GetServices<IIntegrationEventHandler>().Single(h => h.EventType == eventType);
                var context = new IntegrationEventContext(fact.Id, eventType, 1, fact.TenantId, null, DateTimeOffset.UtcNow);
                await handler.HandleAsync(context, fact.Payload, default); await handler.HandleAsync(context, fact.Payload, default);
                Assert.AreEqual("released", await StateAsync(snapshot.ClaimKey));
                // 模拟上传端复用同一 FileId：当前代际与 CAS 失败的另一代际同时留下 Pending。
                writer.SetHost();
                var currentKey = GenerationKey(Guid.CreateVersion7()); var failedKey = GenerationKey(Guid.CreateVersion7());
                var claims = services.GetRequiredService<IHostFileReferenceClaimService>();
                foreach (var key in new[] { currentKey, failedKey })
                    Assert.IsTrue((await claims.ClaimAsync(new(key, kind == "logo" ? "tenancy" : "identity", subject, snapshot.FileId))).IsSuccess);
                await db.ExecuteAsync($"UPDATE {table} SET {prefix}FileId = @File, {prefix}ClaimKey = @Key, Version = Version + 1 WHERE {idColumn} = @Id",
                    new { File = snapshot.FileId, Key = currentKey, Id = subject });
                object old = provider == DatabaseProvider.MySql ? DateTime.UtcNow.AddHours(-2) : DateTimeOffset.UtcNow.AddHours(-2);
                await db.ExecuteAsync("UPDATE fn_files_file_reference_claim SET CreatedAtUtc = @Old, UpdatedAtUtc = @Old WHERE IdempotencyKey IN @Keys",
                    new { Old = old, Keys = new[] { currentKey, failedKey } });
                var runner = new PendingHostFileReferenceClaimReconciliationRunner(services.GetRequiredService<IQueryExecutor>(), services.GetRequiredService<ICommandExecutor>(),
                    services.GetServices<IHostFileReferenceClaimProbe>(), services.GetRequiredService<IOptions<DatabaseOptions>>(), services.GetRequiredService<IClock>());
                var reconciled = await runner.RunOnceAsync(new PendingHostFileReferenceClaimReconciliationOptions {
                    BatchSize = 10, MaxBatchesPerRun = 1, MinimumAgeSeconds = 30, ReleaseGraceSeconds = 60 }, default);
                Assert.AreEqual(1, reconciled.Promoted); Assert.AreEqual(1, reconciled.Released); Assert.AreEqual(0, reconciled.ProbeFailures);
                Assert.AreEqual("active", await StateAsync(currentKey)); Assert.AreEqual("released", await StateAsync(failedKey));
                await handler.HandleAsync(context, fact.Payload, default);
                Assert.AreEqual("active", await StateAsync(currentKey), "旧事实重复投递不得释放重新绑定的当前代际。");

                string GenerationKey(Guid generation) => kind switch
                {
                    "avatar" => HostFileReferenceClaimIdempotencyKeys.IdentityUserAvatar(user, snapshot.FileId, generation),
                    "signature" => HostFileReferenceClaimIdempotencyKeys.IdentityUserSignature(user, snapshot.FileId, generation),
                    _ => HostFileReferenceClaimIdempotencyKeys.TenancyTenantLogo(tenant, snapshot.FileId, generation),
                };
            }
            finally { writer.Clear(); }

            Task<string> StateAsync(string key) => db.QuerySingleAsync<string>("SELECT State FROM fn_files_file_reference_claim WHERE IdempotencyKey = @Key", new { Key = key });
            async Task DeleteAsync(IOutboxWriter outbox)
            {
                var failedRelease = new FailFastRelease(services.GetRequiredService<IHostFileReferenceClaimService>());
                if (kind == "logo")
                {
                    var service = new TenantBrandingMediaService(services.GetRequiredService<IQueryExecutor>(), services.GetRequiredService<ICommandExecutor>(),
                        services.GetRequiredService<ICommandTransaction>(), services.GetRequiredService<IClock>(), writer,
                        services.GetRequiredService<TenantBrandingService>(), services.GetRequiredService<TenantCacheInvalidator>(),
                        hostFileReferenceClaimService: failedRelease, outboxWriter: outbox);
                    var result = writer.IsHost ? await service.DeleteLogoByTenantIdAsync(tenant) : await service.DeleteLogoCurrentAsync(tenant);
                    Assert.IsTrue(result.IsSuccess);
                }
                else
                {
                    var service = new SelfServiceProfileMediaService(services.GetRequiredService<IQueryExecutor>(), services.GetRequiredService<ICommandExecutor>(),
                        services.GetRequiredService<ICommandTransaction>(), services.GetRequiredService<SelfServiceProfileService>(), writer,
                        hostFileReferenceClaimService: failedRelease, outboxWriter: outbox);
                    var result = kind == "avatar" ? await service.DeleteAvatarAsync(user, "host") : await service.DeleteSignatureAsync(user, "host");
                    Assert.IsTrue(result.IsSuccess);
                }
            }
        }
    }

    private sealed class Binding { public Guid FileId { get; set; } public string ClaimKey { get; set; } = ""; public int Version { get; set; } }
    private sealed class Fact { public Guid Id { get; set; } public Guid? TenantId { get; set; } public byte[] Payload { get; set; } = []; }
    private sealed class FailAfterWrite(IOutboxWriter inner) : IOutboxWriter
    {
        public async Task AddAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(string type, int version, T payload, CancellationToken ct = default)
        { await inner.AddAsync(type, version, payload, ct); throw new IOException("owned media fact rollback"); }
        public Task AddAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(string type, int version, T payload, IntegrationEventMetadata metadata, CancellationToken ct = default) =>
            throw new InvalidOperationException("业务媒体不能覆盖可信元数据。");
    }
    private sealed class FailFastRelease(IHostFileReferenceClaimService inner) : IHostFileReferenceClaimService
    {
        public Task<Result<HostFileReferenceClaimResult>> ClaimAsync(HostFileReferenceClaimRequest request, CancellationToken ct = default) => inner.ClaimAsync(request, ct);
        public Task<Result<HostFileReferenceClaimResult>> ConfirmAsync(string key, CancellationToken ct = default) => inner.ConfirmAsync(key, ct);
        public Task<bool> HasOpenClaimsAsync(Guid id, CancellationToken ct = default) => inner.HasOpenClaimsAsync(id, ct);
        public Task<Result<bool>> ReleaseAsync(string key, CancellationToken ct = default) => Task.FromResult(Result<bool>.Failure(new Error("test.release_offline", "Unavailable", ErrorType.Conflict)));
    }
}
