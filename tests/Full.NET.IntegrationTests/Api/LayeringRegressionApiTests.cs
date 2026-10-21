using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Composition;
using Full.NET.Data.Abstractions;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.DataApproval.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Features.ChangePassword;
using Full.NET.Modules.Identity.Http;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.IntegrationTests.Api;

/// <summary>分层修复在真实双库、命令分发器和裁剪宿主中的回归证据。</summary>
[TestClass]
public sealed class LayeringRegressionApiTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Password_late_rejection_rolls_back_business_writes_but_security_rejections_persist(DatabaseProvider provider)
    {
        var fault = new SessionReadFault();
        using var factory = new FullNetApiFactory(provider, await DatabaseAsync(provider),
            configureTestServices: services => DecorateQueries(services, fault));
        await factory.InitializeAsync();
        var identity = await factory.CreateHostIdentityAsync("layering-password", [], password: FullNetApiFactory.TestPassword);
        using var client = factory.CreateClientForHost("localhost");
        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(identity.Username, "wrong-password")),
        };
        login.Headers.Add("Origin", "http://localhost");
        using var loginResponse = await client.SendAsync(login);
        Assert.AreEqual(HttpStatusCode.Unauthorized, loginResponse.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var queries = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
        var before = await queries.QuerySingleOrDefaultAsync<IdentityUserRecord>(IdentitySql.FindHostUserById, new { UserId = identity.UserId });
        Assert.IsNotNull(before);
        Assert.AreEqual(1, before.FailedLoginCount);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new JsonWebToken(identity.AccessToken).Claims, "test"));
        var dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
        var rejected = await dispatcher.SendAsync<Command, ChangePasswordSessionResult>(new Command(
            "wrong-password", "FullNet!2026Replacement", principal, new ClientRequestContext("127.0.0.1", "layering-test")));
        Assert.AreEqual(IdentityErrorCodes.CurrentPasswordInvalid, rejected.Error?.Code);
        var audits = await queries.QuerySingleOrDefaultAsync<long>(new SqlStatement("integration.layering.password_audit",
            "SELECT COUNT(*) FROM fn_identity_auth_audit WHERE UserId = @UserId AND ResultCode = @Code", SqlDataScope.HostOnly),
            new { UserId = identity.UserId, Code = IdentityErrorCodes.CurrentPasswordInvalid });
        Assert.AreEqual(1L, audits);

        // 初检通过、密码及会话写入后，第二次权威读取观察到会话失效。
        fault.Enabled = true;
        var late = await dispatcher.SendAsync<Command, ChangePasswordSessionResult>(new Command(
            FullNetApiFactory.TestPassword, "FullNet!2026Replacement", principal, new ClientRequestContext("127.0.0.1", "layering-test")));
        fault.Enabled = false;
        Assert.AreEqual(IdentityErrorCodes.SessionNotActive, late.Error?.Code);
        var after = await queries.QuerySingleOrDefaultAsync<IdentityUserRecord>(IdentitySql.FindHostUserById, new { UserId = identity.UserId });
        Assert.IsNotNull(after);
        Assert.AreEqual(before.PasswordHash, after.PasswordHash);
        Assert.AreEqual(before.SecurityStamp, after.SecurityStamp);
        Assert.AreEqual(before.Version, after.Version);
        Assert.AreEqual(before.FailedLoginCount, after.FailedLoginCount);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Approval_without_serial_numbers_resolves_and_official_host_excludes_sample(DatabaseProvider provider)
    {
        var settings = FullNetModuleSelection.OfficialModuleNames.Where(name => name != "SerialNumbers")
            .Select((name, index) => new KeyValuePair<string, string?>($"FullNet:Modules:Enabled:{index}", name))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        using var factory = new FullNetApiFactory(provider, await DatabaseAsync(provider), settings);
        await factory.InitializeAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<IDataApprovalSubmissionPort>());
        Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<IDataApprovalScenarioPolicyPort>());
        var catalog = scope.ServiceProvider.GetRequiredService<IFullNetModuleCatalog>();
        Assert.IsFalse(catalog.List().Any(module => module.ModuleKey is "SerialNumbers" or "EnterpriseRequest"));
        using var client = factory.CreateClientForHost("localhost");
        using var response = await client.GetAsync("/api/v1/enterprise_request/enterprise-requests");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static Task<string> DatabaseAsync(DatabaseProvider provider) => provider == DatabaseProvider.SqlServer
        ? SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : SharedDatabaseFixture.CreateMySqlDatabaseAsync();

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Nested_default_transaction_adapter_rejection_cannot_commit_password_write(DatabaseProvider provider)
    {
        using var factory = new FullNetApiFactory(provider, await DatabaseAsync(provider));
        await factory.InitializeAsync();
        var identity = await factory.CreateHostIdentityAsync("layering-nested", [], password: FullNetApiFactory.TestPassword);
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var queries = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
        var commands = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
        var owner = scope.ServiceProvider.GetRequiredService<ICommandTransaction>();
        ICommandTransaction adapter = new DefaultTransactionAdapter(owner);
        var before = await queries.QuerySingleOrDefaultAsync<IdentityUserRecord>(IdentitySql.FindHostUserById, new { UserId = identity.UserId });
        Assert.IsNotNull(before);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => owner.ExecuteAsync(async token =>
        {
            var rejected = await adapter.ExecuteResultAsync(async nested =>
            {
                var affected = await commands.ExecuteAsync(IdentitySql.ResetUserPasswordByIdentity,
                    new { UserId = identity.UserId, ScopeKey = "host", PasswordHash = "uncommitted", SecurityStamp = "uncommitted",
                        PasswordChangedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow }, nested);
                Assert.AreEqual(1, affected);
                return Result<bool>.Failure(new Error("integration.rejected", "Rejected", ErrorType.Conflict));
            }, token);
            Assert.IsFalse(rejected.IsSuccess);
            return true;
        }, CancellationToken.None));
        var after = await queries.QuerySingleOrDefaultAsync<IdentityUserRecord>(IdentitySql.FindHostUserById, new { UserId = identity.UserId });
        Assert.IsNotNull(after);
        Assert.AreEqual(before.PasswordHash, after.PasswordHash);
        Assert.AreEqual(before.SecurityStamp, after.SecurityStamp);
        Assert.IsTrue((await owner.ExecuteResultAsync(_ => Task.FromResult(Result<bool>.Success(true)), CancellationToken.None)).IsSuccess);
    }

    private sealed class DefaultTransactionAdapter(ICommandTransaction inner) : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken) =>
            inner.ExecuteAsync(action, cancellationToken);
    }

    private static void DecorateQueries(IServiceCollection services, SessionReadFault fault)
    {
        var original = services.Last(descriptor => descriptor.ServiceType == typeof(IQueryExecutor));
        services.RemoveAll<IQueryExecutor>();
        services.AddScoped<IQueryExecutor>(provider => new FaultingQueries(
            (IQueryExecutor)(original.ImplementationFactory?.Invoke(provider)
                ?? original.ImplementationInstance
                ?? ActivatorUtilities.CreateInstance(provider, original.ImplementationType!)), fault));
    }

    private sealed class SessionReadFault
    {
        public bool Enabled { get; set; }
        public int Reads { get; set; }
    }

    private sealed class FaultingQueries(IQueryExecutor inner, SessionReadFault fault) : IQueryExecutor
    {
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            if (fault.Enabled && statement == IdentitySql.FindRefreshSessionById && ++fault.Reads == 2)
                return Task.FromResult<T?>(default);
            return inner.QuerySingleOrDefaultAsync<T>(statement, parameters, cancellationToken);
        }
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) =>
            inner.QueryAsync<T>(statement, parameters, cancellationToken);
    }
}
