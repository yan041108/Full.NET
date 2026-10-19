using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>真实 JWT 认证与异常管线：依赖过载返回可重试拒绝，恢复后同一令牌仍可用，撤销后仍拒绝。</summary>
[TestClass]
[DoNotParallelize]
public sealed class SessionAuthorizationCapacityTests
{
    [TestMethod]
    public Task SqlServer_session_capacity_preserves_retry_and_revocation_contract() => VerifyAsync(DatabaseProvider.SqlServer);

    [TestMethod]
    public Task MySql_session_capacity_preserves_retry_and_revocation_contract() => VerifyAsync(DatabaseProvider.MySql);

    private static async Task VerifyAsync(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var fault = new SessionReadFault();
        using var factory = new FullNetApiFactory(provider, connection, IdentityOidcProtocolAssertions.Settings,
            configureTestServices: services =>
            {
                var original = services.Last(d => d.ServiceType == typeof(IQueryExecutor));
                services.RemoveAll<IQueryExecutor>();
                services.Add(new ServiceDescriptor(typeof(IQueryExecutor), sp => new FaultingQuery(
                    (IQueryExecutor)(original.ImplementationInstance ?? original.ImplementationFactory?.Invoke(sp)
                        ?? ActivatorUtilities.CreateInstance(sp, original.ImplementationType!)), fault), original.Lifetime));
            });
        await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("localhost");
        var ordinary = await factory.CreateHostIdentityAsync($"capacity-{Guid.CreateVersion7():N}", []);
        var oidc = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(client,
            IdentityOidcRelyingPartyFixture.PublicClientId, IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null, "admin", FullNetApiFactory.TestPassword, requestOfflineAccess: false);
        Assert.IsFalse(string.IsNullOrWhiteSpace(oidc.AccessToken), oidc.RawTokenResponse);

        foreach (var token in new[] { ordinary.AccessToken, oidc.AccessToken })
        {
            using (var initial = await ReadMeAsync(client, token)) Assert.AreEqual(HttpStatusCode.OK, initial.StatusCode);
            foreach (var kind in new[] { ServiceCapacityFailureKind.Rejected, ServiceCapacityFailureKind.Timeout })
            {
                // 故障只进入权威会话读，不伪造 JWT 校验结果或替换整个认证 Handler。
                fault.Failure = new ServiceCapacityExceededException(kind, TimeSpan.FromMilliseconds(250));
                using var response = await ReadMeAsync(client, token);
                var body = await response.Content.ReadAsStringAsync();
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode, body);
                Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
                Assert.AreEqual(TimeSpan.FromSeconds(1), response.Headers.RetryAfter?.Delta);
                Assert.IsFalse(response.Headers.WwwAuthenticate.Any(), "依赖过载不能触发 invalid_token challenge。");
                using var problem = JsonDocument.Parse(body);
                Assert.AreEqual(CommonErrorCodes.DatabaseCapacityExhausted, problem.RootElement.GetProperty("code").GetString());
                Assert.IsTrue(problem.RootElement.TryGetProperty("traceId", out _));
                Assert.IsFalse(body.Contains(nameof(ServiceCapacityExceededException), StringComparison.Ordinal));
                Assert.IsFalse(body.Contains("connection", StringComparison.OrdinalIgnoreCase));
            }
            fault.Failure = new InvalidOperationException("Simulated session source outage.");
            using (var outage = await ReadMeAsync(client, token)) Assert.AreEqual(HttpStatusCode.Unauthorized, outage.StatusCode);
            fault.Failure = null;
            using (var recovered = await ReadMeAsync(client, token)) Assert.AreEqual(HttpStatusCode.OK, recovered.StatusCode);
        }
        Assert.IsTrue(fault.OrdinaryCapacityReads >= 2 && fault.OidcCapacityReads >= 2,
            "必须分别真实到达普通及 OIDC 会话的两种容量失败分支。");

        // 通过正式 SQL 执行器持久化撤销；不能以缓存或测试故障返回值替代撤销权威源。
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
            var command = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
            var now = DateTimeOffset.UtcNow;
            Assert.AreEqual(1, await command.ExecuteAsync(IdentitySql.RevokeAllUserSessions,
                IdentitySqlParameters.Create(("UserId", ordinary.UserId), ("RevokedAtUtc", now))));
            var applicationSessionClaim = IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(
                oidc.AccessToken, FullNetIdentityClaimTypes.ApplicationSessionId);
            Assert.IsNotNull(applicationSessionClaim);
            var applicationSessionId = Guid.Parse(applicationSessionClaim);
            Assert.AreEqual(1, await command.ExecuteAsync(IdentityOidcSessionSql.RevokeApplicationSession,
                IdentitySqlParameters.Create(("Id", applicationSessionId), ("RevokedAtUtc", now), ("UpdatedAtUtc", now))));
        }
        foreach (var token in new[] { ordinary.AccessToken, oidc.AccessToken, "invalid-token" })
        {
            using var denied = await ReadMeAsync(client, token);
            Assert.AreEqual(HttpStatusCode.Unauthorized, denied.StatusCode);
            Assert.IsNull(denied.Headers.RetryAfter);
        }
    }

    private static async Task<HttpResponseMessage> ReadMeAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private sealed class SessionReadFault
    {
        internal Exception? Failure;
        internal int OrdinaryCapacityReads;
        internal int OidcCapacityReads;
    }

    private sealed class FaultingQuery(IQueryExecutor inner, SessionReadFault fault) : IQueryExecutor
    {
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (statement.Name == IdentitySql.FindRefreshSessionById.Name
                || statement.Name == IdentityOidcSessionSql.FindApplicationSessionValidationById.Name)
            {
                if (fault.Failure is ServiceCapacityExceededException)
                {
                    if (statement.Name == IdentitySql.FindRefreshSessionById.Name) Interlocked.Increment(ref fault.OrdinaryCapacityReads);
                    else Interlocked.Increment(ref fault.OidcCapacityReads);
                }
                if (fault.Failure is { } failure) return Task.FromException<T?>(failure);
            }
            return inner.QuerySingleOrDefaultAsync<T>(statement, parameters, cancellationToken);
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null,
            CancellationToken cancellationToken = default) => inner.QueryAsync<T>(statement, parameters, cancellationToken);
    }
}
