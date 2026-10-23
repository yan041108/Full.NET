using System.Data.Common;
using System.Net;
using System.Text.Json;
using Dapper;
using DbUp;
using DbUp.Helpers;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Api;
using Full.NET.Migrations.DbUp;
using Full.NET.Modules.Notifications.Contracts;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Notifications;

/// <summary>复用双库纵向夹具验证单项授权、直接调用拒绝及权限迁移不扩大普通角色权限。</summary>
internal static class NotificationGovernanceAuthorizationAssertions
{
    internal static async Task VerifyAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        foreach (var resource in new[] { "producer_scenes", "quotas" })
        {
            var path = resource == "producer_scenes"
                ? "/api/v1/notifications/producer-scenes"
                : "/api/v1/notifications/quotas";
            var read = $"notifications.{resource}.read";
            var createToken = await factory.CreateHostAccessTokenAsync(
                [read, $"notifications.{resource}.create"], cancellationToken);
            var updateToken = await factory.CreateHostAccessTokenAsync(
                [read, $"notifications.{resource}.update"], cancellationToken);
            var enabledToken = await factory.CreateHostAccessTokenAsync(
                [read, $"notifications.{resource}.set_enabled"], cancellationToken);
            var nonce = Guid.CreateVersion7().ToString("N");
            object create = resource == "producer_scenes"
                ? new CreateNotificationProducerSceneRequest($"guard-{nonce}", "authorization",
                    "transactional", ["inbox"], false)
                : new CreateNotificationQuotaPolicyRequest("provider", $"guard-{nonce}", 60, 10);
            using var created = await SendAsync(client, createToken, HttpMethod.Post,
                path, create, cancellationToken);
            Assert.AreEqual(HttpStatusCode.Created, created.StatusCode,
                await created.Content.ReadAsStringAsync(cancellationToken));
            using var document = JsonDocument.Parse(await created.Content.ReadAsStringAsync(cancellationToken));
            var id = document.RootElement.GetProperty("id").GetGuid();
            var version = document.RootElement.GetProperty("version").GetInt64();
            object update = resource == "producer_scenes"
                ? new UpdateNotificationProducerSceneRequest("transactional", ["inbox"], false, version)
                : new UpdateNotificationQuotaPolicyRequest(60, 11, version);
            object enabled = resource == "producer_scenes"
                ? new SetNotificationProducerSceneEnabledRequest(false, version)
                : new SetNotificationQuotaPolicyEnabledRequest(false, version);

            // 单项权限不能隐式授权其他操作，绕过按钮直接调用也必须在 Endpoint 被拒绝。
            await AssertDeniedAsync(createToken, HttpMethod.Put, $"{path}/{id:D}", update);
            await AssertDeniedAsync(createToken, HttpMethod.Post, $"{path}/{id:D}/enabled", enabled);
            await AssertDeniedAsync(updateToken, HttpMethod.Post, path, create);
            await AssertDeniedAsync(updateToken, HttpMethod.Post, $"{path}/{id:D}/enabled", enabled);
            await AssertDeniedAsync(enabledToken, HttpMethod.Post, path, create);
            await AssertDeniedAsync(enabledToken, HttpMethod.Put, $"{path}/{id:D}", update);

            using var updated = await SendAsync(client, updateToken, HttpMethod.Put,
                $"{path}/{id:D}", update, cancellationToken);
            Assert.AreEqual(HttpStatusCode.OK, updated.StatusCode,
                await updated.Content.ReadAsStringAsync(cancellationToken));
            using var updatedDocument = JsonDocument.Parse(await updated.Content.ReadAsStringAsync(cancellationToken));
            version = updatedDocument.RootElement.GetProperty("version").GetInt64();
            enabled = resource == "producer_scenes"
                ? new SetNotificationProducerSceneEnabledRequest(false, version)
                : new SetNotificationQuotaPolicyEnabledRequest(false, version);
            using var disabled = await SendAsync(client, enabledToken, HttpMethod.Post,
                $"{path}/{id:D}/enabled", enabled, cancellationToken);
            Assert.AreEqual(HttpStatusCode.OK, disabled.StatusCode,
                await disabled.Content.ReadAsStringAsync(cancellationToken));

            async Task AssertDeniedAsync(string token, HttpMethod method, string target, object body)
            {
                using var response = await SendAsync(client, token, method, target, body, cancellationToken);
                Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
                using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                Assert.AreEqual("authorization.permission_denied",
                    problem.RootElement.GetProperty("code").GetString());
            }
        }

        // 已存在多个各自仅获单项授权的普通角色，固定脚本重跑不得向这些角色追加其他权限。
        await using DbConnection connection = factory.Provider == DatabaseProvider.SqlServer
            ? new SqlConnection(factory.ConnectionString)
            : new MySqlConnection(MySqlConnectionStringPolicy.Create(
                factory.ConnectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: false));
        var permissionsBefore = await connection.QuerySingleAsync<long>(
            "SELECT COUNT(*) FROM fn_identity_role_permission");
        for (var repeat = 0; repeat < 2; repeat++)
        {
            var builder = factory.Provider == DatabaseProvider.SqlServer
                ? DeployChanges.To.SqlDatabase(factory.ConnectionString)
                : DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(
                    factory.ConnectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: true));
            var segment = factory.Provider == DatabaseProvider.SqlServer
                ? ".Migrations.SqlServer." : ".Migrations.MySql.";
            var result = builder.WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly,
                    name => name.Contains(segment, StringComparison.Ordinal)
                        && (name.EndsWith(".280_NotificationsProducerSceneCatalogPermissions.sql", StringComparison.Ordinal)
                            || name.EndsWith(".282_NotificationsQuotaPermissions.sql", StringComparison.Ordinal)))
                .JournalTo(new NullJournal())
                .WithExecutionTimeout(TimeSpan.FromSeconds(60))
                .Build().PerformUpgrade();
            Assert.IsTrue(result.Successful, result.Error?.ToString());
            Assert.HasCount(2, result.Scripts);
            Assert.AreEqual(permissionsBefore, await connection.QuerySingleAsync<long>(
                "SELECT COUNT(*) FROM fn_identity_role_permission"),
                "新控制面权限必须显式授予，迁移重跑不得扩大普通角色授权。");
        }
    }

    /// <summary>每次请求独立携带受限令牌并释放请求对象，避免共享客户端身份污染其他授权断言。</summary>
    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        string token,
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = NotificationProfileBindingAssertions.CreateBearerJsonRequest(
            method, path, token, body);
        return await client.SendAsync(request, cancellationToken);
    }
}
