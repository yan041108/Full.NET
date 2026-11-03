using System.Security.Cryptography;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Seeding;
using Full.NET.Seeding.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using System.Data.Common;
using Dapper;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>同场景双库验证客户端受控初始化与跨节点持久化签名切换。</summary>
[TestClass]
public sealed class IdentityOidcGovernancePersistenceTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Signing_policy_migration_recovers_unrecorded_schema_without_resetting_active_key(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await using DbConnection database = provider == DatabaseProvider.SqlServer
            ? new SqlConnection(connection)
            : new MySqlConnection(MySqlConnectionStringPolicy.Create(connection, MySqlGuidStorageMode.Binary16, allowUserVariables: false));
        var assembly = typeof(DbUpMigrationRunner).Assembly;
        var name = assembly.GetManifestResourceNames().Single(item => item.EndsWith(
            $"{provider}.291_IdentityOidcSigningPolicy.sql", StringComparison.Ordinal));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
        var script = await reader.ReadToEndAsync();
        var metadataOffset = script.IndexOf("IF NOT EXISTS (SELECT 1 FROM sys.extended_properties", StringComparison.Ordinal);
        // SQL Server 模拟建表提交但注释/DbUp 记账未完成；MySQL 的 DDL 本身独立提交。
        await database.ExecuteAsync(metadataOffset > 0 ? script[..metadataOffset] : script);
        await database.ExecuteAsync("""
            INSERT INTO fn_identity_oidc_signing_policy (Id, PolicyKey, ActiveKeyId, Version, UpdatedAtUtc)
            VALUES (@Id, 'default', 'preserved', 7, @Now)
            """, new { Id = Guid.CreateVersion7(), Now = DateTime.UtcNow });
        await database.ExecuteAsync(script);
        await database.ExecuteAsync(script);
        Assert.AreEqual("preserved", await database.QuerySingleAsync<string>(
            "SELECT ActiveKeyId FROM fn_identity_oidc_signing_policy WHERE PolicyKey = 'default'"));
        Assert.AreEqual(7, await database.QuerySingleAsync<int>(
            "SELECT Version FROM fn_identity_oidc_signing_policy WHERE PolicyKey = 'default'"));
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Managed_policy_survives_seed_replay_and_signing_activation_survives_node_restart(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var first = RSA.Create(2048);
        using var second = RSA.Create(2048);
        var settings = new Dictionary<string, string?>(IdentityOidcProtocolAssertions.Settings)
        {
            ["Identity:Oidc:AllowDevelopmentEphemeralSigningKey"] = "false",
            ["Identity:Oidc:ActiveSigningKeyId"] = "first",
            ["Identity:Oidc:EncryptionKeyBase64"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["Identity:Oidc:SigningKeys:first:PublicKeyPem"] = first.ExportRSAPublicKeyPem(),
            ["Identity:Oidc:SigningKeys:first:PrivateKeyPem"] = first.ExportRSAPrivateKeyPem(),
            ["Identity:Oidc:SigningKeys:second:PublicKeyPem"] = second.ExportRSAPublicKeyPem(),
            ["Identity:Oidc:SigningKeys:second:PrivateKeyPem"] = second.ExportRSAPrivateKeyPem()
        };
        using var nodeA = new FullNetApiFactory(provider, connection, settings);
        await nodeA.InitializeAsync();
        await using (var scope = nodeA.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            var client = await manager.FindByClientIdAsync(IdentityOidcRelyingPartyFixture.PublicClientId);
            Assert.IsNotNull(client);
            var descriptor = new OpenIddictApplicationDescriptor();
            await manager.PopulateAsync(descriptor, client);
            descriptor.DisplayName = "保留管理员修改";
            descriptor.Permissions.Remove(OpenIddictConstants.Permissions.GrantTypes.RefreshToken);
            IdentityOidcClientMetadata.Write(descriptor, false, "restricted-api", true);
            await manager.UpdateAsync(client, descriptor);
            var seed = scope.ServiceProvider.GetServices<IDataSeedContributor>().OfType<IdentityOidcClientSeedContributor>().Single();
            await seed.SeedAsync(new(Guid.CreateVersion7(), SeedProfile.Baseline, "Testing", "zh-CN", "oidc-replay"));
            client = await manager.FindByClientIdAsync(IdentityOidcRelyingPartyFixture.PublicClientId);
            Assert.IsNotNull(client);
            Assert.AreEqual("保留管理员修改", await manager.GetDisplayNameAsync(client));
            Assert.IsTrue((await scope.ServiceProvider.GetRequiredService<IdentityOidcClientConfigResolver>()
                .ResolveAsync(IdentityOidcRelyingPartyFixture.PublicClientId))!.IsDisabled);
            Assert.IsFalse(await manager.HasPermissionAsync(client, OpenIddictConstants.Permissions.GrantTypes.RefreshToken));
            await manager.CreateAsync(IdentityOidcClientDescriptorFactory.Build(new(
                "openid-only", null, "仅身份 scope", [IdentityOidcRelyingPartyFixture.PublicRedirectUri], [], ["openid"], false, null)));
            await manager.CreateAsync(IdentityOidcClientDescriptorFactory.Build(new(
                "profile-only", null, "不允许身份 scope", [IdentityOidcRelyingPartyFixture.PublicRedirectUri], [], ["profile"], false, null)));
            Assert.IsTrue(await scope.ServiceProvider.GetRequiredService<IdentityOidcSigningPolicy>().ActivateAsync("second", default));
        }
        using (var browser = nodeA.CreateClientForHost("localhost"))
        {
            foreach (var (clientId, deniedScope) in new[]
                { ("openid-only", "profile"), ("openid-only", "offline_access"), ("profile-only", "openid") })
            {
                using var response = await browser.GetAsync($"/connect/authorize?client_id={clientId}&response_type=code"
                    + $"&redirect_uri={Uri.EscapeDataString(IdentityOidcRelyingPartyFixture.PublicRedirectUri)}"
                    + $"&scope=openid%20{deniedScope}&state=scope-boundary&code_challenge={new string('A', 43)}&code_challenge_method=S256");
                var protocolError = response.Headers.Location?.Query ?? await response.Content.ReadAsStringAsync();
                StringAssert.Contains(protocolError, "invalid_scope");
                Assert.IsTrue(response.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Found
                    or System.Net.HttpStatusCode.SeeOther);
            }
        }
        // 新节点仍使用 first 启动配置，必须以数据库指针为准。
        using var nodeB = new FullNetApiFactory(provider, connection, settings);
        await nodeB.InitializeAsync();
        await using var secondScope = nodeB.Services.CreateAsyncScope();
        secondScope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        await secondScope.ServiceProvider.GetRequiredService<IdentityOidcSigningPolicy>().SynchronizeAsync(default);
        Assert.AreEqual("second", secondScope.ServiceProvider.GetRequiredService<IdentityOidcSigningKeyRing>().ActiveSigningKeyId);
        Assert.AreEqual("second", await secondScope.ServiceProvider.GetRequiredService<IQueryExecutor>()
            .QuerySingleOrDefaultAsync<string>(IdentityOidcSigningPolicySql.FindActiveKey));
    }
}
