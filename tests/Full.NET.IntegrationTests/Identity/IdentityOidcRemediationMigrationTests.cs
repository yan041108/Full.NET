using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>重复执行未记账 DDL 保留 BFF 票据、已消费 TOTP 步长与退出终态。</summary>
[TestClass]
public sealed class IdentityOidcRemediationMigrationTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Replayed_browser_mfa_and_logout_schema_preserves_security_state(DatabaseProvider provider)
    {
        var connectionString = await IdentityBrowserTestSupport.DatabaseAsync(provider);
        using var factory = new FullNetApiFactory(provider, connectionString); await factory.InitializeAsync();
        await using DbConnection database = provider == DatabaseProvider.SqlServer ? new SqlConnection(connectionString)
            : new MySqlConnection(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: true));
        await database.ExecuteAsync("""
            INSERT INTO fn_identity_user_totp (UserId, SecretProtected, IsEnabled, CreatedAtUtc, Version, LastAcceptedCounter)
            SELECT Id, 'protected-fixture', 1, @Now, 7, 100 FROM fn_identity_user WHERE ScopeKey = 'host' AND NormalizedUsername = 'ADMIN'
            """, new { Now = DateTime.UtcNow });
        await database.ExecuteAsync("""
            INSERT INTO fn_identity_browser_session (Id, HandleHash, Ciphertext, ExpiresAtUtc, Version)
            VALUES (@Id, @Hash, 'protected-ticket', @Expires, 9)
            """, new { Id = Guid.CreateVersion7(), Hash = new string('A', 64), Expires = DateTime.UtcNow.AddHours(1) });
        await database.ExecuteAsync("""
            INSERT INTO fn_identity_oidc_logout_delivery (Id, CenterSessionId, UserId, ClientId, TargetUri, StatusKey, AttemptCount, NextAttemptAtUtc, CreatedAtUtc)
            VALUES (@Id, @Center, @User, 'rp', 'https://rp.example/logout', 'failed', 8, @Now, @Now)
            """, new { Id = Guid.CreateVersion7(), Center = Guid.CreateVersion7(), User = Guid.CreateVersion7(), Now = DateTime.UtcNow });
        await database.ExecuteAsync("""
            INSERT INTO fn_identity_oidc_signing_key (Id, KeyId, PublicKeyHash, PublishedAtUtc, LastActivatedAtUtc, RetireAfterUtc, IsRetired, Version)
            VALUES (@Id, 'retired-fixture', @Hash, @Now, @Now, @Now, 1, 17)
            """, new { Id = Guid.CreateVersion7(), Hash = new string('A', 64), Now = DateTime.UtcNow });
        for (var replay = 0; replay < 2; replay++)
        {
            // 由正式迁移执行器处理 DELIMITER，且按完整文件名排除其他模块的同编号脚本。
            IdentityMfaSigningMigrationNamingTests.Replay(provider, connectionString,
                "300_IdentityBrowserSession.sql", "301_IdentityOidcMfaCounter.sql",
                "302_IdentityOidcLogoutDelivery.sql", "303_IdentityOidcKeyGovernance.sql");
        }
        Assert.AreEqual(100L, await database.QuerySingleAsync<long>("SELECT LastAcceptedCounter FROM fn_identity_user_totp WHERE SecretProtected = 'protected-fixture'"));
        Assert.AreEqual(9L, await database.QuerySingleAsync<long>("SELECT Version FROM fn_identity_browser_session WHERE Ciphertext = 'protected-ticket'"));
        Assert.AreEqual(8, await database.QuerySingleAsync<int>("SELECT AttemptCount FROM fn_identity_oidc_logout_delivery WHERE ClientId = 'rp'"));
        Assert.IsTrue(await database.QuerySingleAsync<bool>("SELECT IsRetired FROM fn_identity_oidc_signing_key WHERE KeyId = 'retired-fixture'"));
        Assert.AreEqual(17, await database.QuerySingleAsync<int>("SELECT Version FROM fn_identity_oidc_signing_key WHERE KeyId = 'retired-fixture'"));
    }
}
