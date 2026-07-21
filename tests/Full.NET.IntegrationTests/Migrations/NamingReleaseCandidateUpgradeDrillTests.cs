using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>
/// Task 6 Step 2：发布候选升级演练的自动化等价路径。
/// 从 Through009 有数据源库逻辑克隆后执行 010→011，再做登录与可用租户冒烟；
/// 不等同于生产备份介质或 RPO/RTO 签字。
/// </summary>
[TestClass]
public sealed class NamingReleaseCandidateUpgradeDrillTests
{
    // Expand 只把 legacy Type 镜像到 MessageType 列，不改写存量协议值；规范值由新写入路径产生。
    private const string SeededLegacyMessageType = "fullnet.tenancy.tenant-provisioned";
    private const string SeededTenantIdentifier = "naming-expand";

    [TestMethod]
    public async Task NamingReleaseCandidateUpgradeDrill_SqlServer_preserves_data_and_serves_api()
    {
        var source = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        await NamingExpandTestMigrationRunner.MigrateSqlServerThrough009Async(source);
        await using (var sourceConnection = new SqlConnection(source))
        {
            await sourceConnection.OpenAsync();
            await NamingExpandTestData.InsertTenantAndOutboxAsync(sourceConnection);
        }

        var target = await DatabaseLogicalClone.CloneSqlServerThrough009Async(source);
        var expand = await NamingExpandTestMigrationRunner.MigrateSqlServerThrough010Async(target);
        Assert.AreEqual(1, expand.ExecutedScriptCount);

        var contract = await NamingContractTestMigrationRunner
            .CreateSqlServerRunner(target)
            .MigrateAsync();
        Assert.AreEqual(1, contract.ExecutedScriptCount);

        await using (var connection = new SqlConnection(target))
        {
            Assert.IsFalse(await connection.ExecuteScalarAsync<bool>(
                "SELECT CAST(IIF(OBJECT_ID(N'dbo.fn_tenant_tenant', N'U') IS NULL, 0, 1) AS bit)"));
            Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM dbo.fn_tenancy_tenant
                WHERE Identifier = @Identifier
                """,
                new { Identifier = SeededTenantIdentifier }));
            Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM dbo.fn_outbox_message
                WHERE MessageType = @MessageType
                  AND Payload = 0x7B7D
                """,
                new { MessageType = SeededLegacyMessageType }));
            Assert.AreEqual("Contracted", await connection.ExecuteScalarAsync<string>(
                "SELECT SchemaMode FROM dbo.fn_pre_v1_naming_contract_state WHERE Id = 1"));
        }

        await AssertApiSmokeAsync(DatabaseProvider.SqlServer, target);
    }

    [TestMethod]
    public async Task NamingReleaseCandidateUpgradeDrill_MySql_preserves_data_and_serves_api()
    {
        var source = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await NamingExpandTestMigrationRunner.MigrateMySqlThrough009Async(source);
        await using (var sourceConnection = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                source,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false)))
        {
            await sourceConnection.OpenAsync();
            await NamingExpandTestData.InsertTenantAndOutboxAsync(sourceConnection);
        }

        var target = await DatabaseLogicalClone.CloneMySqlThrough009Async(source);
        var expand = await NamingExpandTestMigrationRunner.MigrateMySqlThrough010Async(target);
        Assert.AreEqual(1, expand.ExecutedScriptCount);

        var contract = await NamingContractTestMigrationRunner
            .CreateMySqlRunner(target)
            .MigrateAsync();
        Assert.AreEqual(1, contract.ExecutedScriptCount);

        await using (var connection = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                target,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false)))
        {
            Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = 'fn_tenant_tenant'
                """));
            Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM fn_tenancy_tenant
                WHERE Identifier = @Identifier
                """,
                new { Identifier = SeededTenantIdentifier }));
            Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM fn_outbox_message
                WHERE MessageType = @MessageType
                  AND Payload = X'7B7D'
                """,
                new { MessageType = SeededLegacyMessageType }));
            Assert.AreEqual("Contracted", await connection.ExecuteScalarAsync<string>(
                "SELECT SchemaMode FROM fn_pre_v1_naming_contract_state WHERE Id = 1"));
        }

        await AssertApiSmokeAsync(DatabaseProvider.MySql, target);
    }

    /// <summary>
    /// 在已 Contract 的库上启动 API：幂等补齐后续迁移、引导管理员，再验证登录与升级前租户仍可见。
    /// </summary>
    private static async Task AssertApiSmokeAsync(
        DatabaseProvider provider,
        string connectionString)
    {
        await using var factory = new FullNetApiFactory(provider, connectionString);
        await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("localhost");

        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new
            {
                username = "admin",
                password = FullNetApiFactory.TestPassword,
            }),
        };
        loginRequest.Headers.Add("Origin", "http://localhost");
        using var loginResponse = await client.SendAsync(loginRequest);
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
        var login = await loginResponse.Content.ReadFromJsonAsync<LoginTokenBody>();
        Assert.IsNotNull(login);
        Assert.IsFalse(string.IsNullOrWhiteSpace(login.AccessToken));

        using var availableRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/tenancy/available");
        availableRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            login.AccessToken);
        availableRequest.Headers.Add("Origin", "http://localhost");
        using var availableResponse = await client.SendAsync(availableRequest);
        Assert.AreEqual(HttpStatusCode.OK, availableResponse.StatusCode);
        var available = await availableResponse.Content
            .ReadFromJsonAsync<TenantContextSummary[]>();
        Assert.IsNotNull(available);
        Assert.IsTrue(
            available.Any(tenant => tenant.Identifier == SeededTenantIdentifier),
            "升级前写入的 naming-expand 租户必须在 Contract 后仍可通过 available 列出。");
        Assert.IsTrue(
            available.Any(tenant => tenant.Identifier == "acme"),
            "Initialize 引导的 acme 租户必须同时可用。");
    }

    private sealed record LoginTokenBody(string AccessToken);
}
