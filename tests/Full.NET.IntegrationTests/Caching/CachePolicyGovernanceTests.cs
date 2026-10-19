using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Caching.Abstractions;
using Full.NET.Caching.Fusion;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.ObservabilityAdmin.Contracts;
using Full.NET.Modules.ObservabilityAdmin.Features.ManageCachePolicies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Full.NET.IntegrationTests.Caching;

/// <summary>真实 HTTP 权限、输入边界、成功/失败 B1 落库及缓存失效失败响应，不改变审计可靠性等级。</summary>
[TestClass]
[DoNotParallelize]
public sealed class CachePolicyGovernanceTests
{
    private const string Root = "/api/v1/observability/cache-policies";
    private static readonly string Route = $"{Root}/{CacheEntryNames.GridPreference}/invalidations";

    [TestMethod]
    public Task SqlServer_cache_policy_actions_enforce_permissions_and_persist_success_failure_audit() => VerifyAsync(DatabaseProvider.SqlServer);

    [TestMethod]
    public Task MySql_cache_policy_actions_enforce_permissions_and_persist_success_failure_audit() => VerifyAsync(DatabaseProvider.MySql);

    private static async Task VerifyAsync(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var probe = new FailureProbe();
        using var factory = new FullNetApiFactory(provider, connection, configureTestServices: services =>
        {
            // 健康分支仍调用当前真实失效实现；只在指定操作注入异常，验证 HTTP 与实际审计落库边界。
            var original = services.Single(descriptor => descriptor.ServiceType == typeof(ICacheInvalidator));
            services.RemoveAll<ICacheInvalidator>();
            services.AddSingleton<ICacheInvalidator>(sp => new FaultingInvalidator(
                (ICacheInvalidator)(original.ImplementationInstance
                    ?? original.ImplementationFactory?.Invoke(sp)
                    ?? ActivatorUtilities.CreateInstance(sp, original.ImplementationType!)), probe));
        });
        await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("localhost");
        var reader = await factory.CreateHostIdentityAsync($"cache-reader-{Guid.CreateVersion7():N}", [ObservabilityCachePolicyPermissions.Read]);
        var writer = await factory.CreateHostIdentityAsync($"cache-writer-{Guid.CreateVersion7():N}", [ObservabilityCachePolicyPermissions.Invalidate]);
        var adminToken = await CacheBackgroundAuthorityFixture.LoginAsync(client, "admin");
        var tenant = await CacheBackgroundAuthorityFixture.GetAcmeTenantAsync(client, adminToken);
        var valid = new CacheInvalidationRequest("by-user-grid", new Dictionary<string, string>
        {
            ["userId"] = writer.UserId.ToString("D"), ["gridKey"] = "identity.users",
        }, CacheInvalidationScopeNames.CurrentNodeOnly);
        using (var anonymous = await client.GetAsync(Root)) Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        await SendAsync(HttpMethod.Get, Root, reader.AccessToken, null, HttpStatusCode.OK);
        await SendAsync(HttpMethod.Get, Root, writer.AccessToken, null, HttpStatusCode.Forbidden);
        var callsBefore = probe.Calls;
        await SendAsync(HttpMethod.Post, Route, reader.AccessToken, valid, HttpStatusCode.Forbidden);
        Assert.AreEqual(callsBefore, probe.Calls, "只读权限不得触发缓存执行。");

        foreach (var invalid in new[]
        {
            valid with { OperationKey = "arbitrary-key" },
            valid with { Parameters = new Dictionary<string, string>(valid.Parameters!) { ["key"] = "private-cache-key" } },
            valid with { Parameters = new Dictionary<string, string>(valid.Parameters!) { ["gridKey"] = "unknown.grid" } },
        })
        {
            await SendAsync(HttpMethod.Post, Route, writer.AccessToken, invalid, HttpStatusCode.BadRequest,
                ObservabilityAdminErrorCodes.CacheInvalidationInvalid);
            Assert.AreEqual(callsBefore, probe.Calls, "无效操作、额外参数及未登记网格须先拒绝，不能产生部分失效。");
        }

        await SendAsync(HttpMethod.Post, Route, writer.AccessToken, valid, HttpStatusCode.OK);
        Assert.AreEqual(callsBefore + 1, probe.Calls);
        Assert.HasCount(1, probe.LastTargets);
        var environment = factory.Services.GetRequiredService<IHostEnvironment>().EnvironmentName;
        Assert.AreEqual(new CacheInvalidationTarget(CacheEntryNames.GridPreference,
            CacheKeyBuilder.ForGlobal(environment, "settings", "grid-preference", $"{writer.UserId:N}:identity.users", "v2"),
            CacheInvalidationTargetKind.Key, CacheInvalidationScope.CurrentNodeOnly), probe.LastTargets[0]);
        // 已退役的租户缓存操作不能因具备管理权限而再次启用。
        await SendAsync(HttpMethod.Post, $"{Root}/{CacheEntryNames.TenantResolution}/invalidations", writer.AccessToken,
            new CacheInvalidationRequest("by-tenant", null, null), HttpStatusCode.BadRequest,
            ObservabilityAdminErrorCodes.CachePolicyNotInvalidatable);
        Assert.AreEqual(callsBefore + 1, probe.Calls, "权威读取条目拒绝失效后不能调用缓存执行器。");
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
        await AssertAuditAsync(200, true);

        probe.Throw = true;
        try { await SendAsync(HttpMethod.Post, Route, writer.AccessToken, valid, HttpStatusCode.InternalServerError); }
        finally { probe.Throw = false; }
        Assert.AreEqual(callsBefore + 2, probe.Calls);
        await AssertAuditAsync(500, false);
        // 失败后的下一次真实执行与成功审计仍须可用，不能留下永久拒绝或成功标记污染。
        await SendAsync(HttpMethod.Post, Route, writer.AccessToken, valid, HttpStatusCode.OK);
        Assert.AreEqual(callsBefore + 3, probe.Calls);
        await AssertAuditAsync(200, true, expectedCount: 2);

        var tenantToken = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(client, adminToken, tenant.Id);
        await SendAsync(HttpMethod.Get, Root, tenantToken, null, HttpStatusCode.Forbidden);
        await SendAsync(HttpMethod.Post, Route, tenantToken, valid, HttpStatusCode.Forbidden);
        Assert.AreEqual(callsBefore + 3, probe.Calls, "Host 超级管理员切入租户后也不能使用 Host 缓存管理权限。");

        async Task AssertAuditAsync(int status, bool succeeded, int expectedCount = 1)
        {
            var rows = (await db.QueryAsync<AuditRow>("""
                SELECT HttpMethod, RequestPath, StatusCode, Succeeded, UserId, TenantId, PermissionCode, TraceId, ContextJson
                FROM fn_auditing_operation_log
                WHERE UserId=@UserId AND RequestPath=@Path AND StatusCode=@Status
                """, new { UserId = writer.UserId, Path = Route, Status = status })).ToArray();
            Assert.HasCount(expectedCount, rows, "HTTP 完成时应已等待 B1 写入尝试；本测试权威审计库保持健康。");
            foreach (var row in rows)
            {
                Assert.AreEqual("POST", row.HttpMethod);
                Assert.AreEqual(succeeded, row.Succeeded);
                Assert.AreEqual(writer.UserId, row.UserId);
                Assert.IsNull(row.TenantId);
                Assert.AreEqual(ObservabilityCachePolicyPermissions.Invalidate, row.PermissionCode);
                Assert.IsFalse(string.IsNullOrWhiteSpace(row.TraceId));
                var details = row.ContextJson ?? "";
                Assert.DoesNotContain(writer.AccessToken, details, StringComparison.Ordinal);
                Assert.DoesNotContain("private-cache-key", details, StringComparison.Ordinal);
                Assert.DoesNotContain(FailureProbe.Marker, details, StringComparison.Ordinal);
            }
        }

        async Task SendAsync(HttpMethod method, string route, string token, CacheInvalidationRequest? body,
            HttpStatusCode status, string? problemCode = null)
        {
            using var request = new HttpRequestMessage(method, route);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            Assert.AreEqual(status, response.StatusCode, text);
            if ((int)status >= 400)
            {
                Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
                using var problem = JsonDocument.Parse(text);
                Assert.AreEqual((int)status, problem.RootElement.GetProperty("status").GetInt32());
                if (problemCode is not null) Assert.AreEqual(problemCode, problem.RootElement.GetProperty("code").GetString());
                Assert.DoesNotContain(FailureProbe.Marker, text, StringComparison.Ordinal);
                Assert.DoesNotContain(token, text, StringComparison.Ordinal);
            }
        }
    }

    private sealed class FailureProbe
    {
        internal const string Marker = "cache-policy-private-fixture-failure";
        internal bool Throw;
        internal int Calls;
        internal CacheInvalidationTarget[] LastTargets = [];
    }

    private sealed class FaultingInvalidator(ICacheInvalidator inner, FailureProbe probe) : ICacheInvalidator, IDisposable
    {
        public ValueTask RemoveAsync(string entryName, string key, CacheInvalidationScope scope,
            CancellationToken cancellationToken = default)
        {
            probe.Calls++;
            probe.LastTargets = [new(entryName, key, CacheInvalidationTargetKind.Key, scope)];
            if (probe.Throw) return ValueTask.FromException(new InvalidOperationException(FailureProbe.Marker));
            return inner.RemoveAsync(entryName, key, scope, cancellationToken);
        }

        public ValueTask RemoveByTagAsync(string entryName, string tag, CacheInvalidationScope scope,
            CancellationToken cancellationToken = default) => inner.RemoveByTagAsync(entryName, tag, scope, cancellationToken);

        public void Dispose() => (inner as IDisposable)?.Dispose();
    }

    private sealed class AuditRow
    {
        public string HttpMethod { get; set; } = "";
        public string RequestPath { get; set; } = "";
        public int StatusCode { get; set; }
        public bool Succeeded { get; set; }
        public Guid UserId { get; set; }
        public Guid? TenantId { get; set; }
        public string? PermissionCode { get; set; }
        public string? TraceId { get; set; }
        public string? ContextJson { get; set; }
    }
}
