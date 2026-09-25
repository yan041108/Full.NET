using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.IntegrationTests.Api;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>认证状态变更后能够经受保护 API 查询到事件，且响应不泄露原始身份材料。</summary>
internal static class AuthenticationEventAssertions
{
    public static async Task VerifyAsync(FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        // 每次从空库执行当前迁移，避免旧 schema 模板仅凭 journal 数量复用旧版 235。
        await factory.InitializeAsync(cancellationToken, useSchemaTemplate: false);
        if (factory.Provider == DatabaseProvider.SqlServer)
        {
            await using var connection = new SqlConnection(factory.ConnectionString);
            var remaining = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition("SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.fn_identity_auth_audit') AND name = N'FK_fn_identity_auth_audit_User'", cancellationToken: cancellationToken));
            Assert.AreEqual(0, remaining, "认证审计历史不应再依赖用户外键。");
        }
        else
        {
            await using var connection = new MySqlConnection(factory.ConnectionString);
            var remaining = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition("SELECT COUNT(*) FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_identity_auth_audit' AND CONSTRAINT_NAME = 'FK_fn_identity_auth_audit_User'", cancellationToken: cancellationToken));
            Assert.AreEqual(0, remaining, "认证审计历史不应再依赖用户外键。");
        }
        using var client = factory.CreateClient();
        using var anonymousResponse = await client.GetAsync(
            "/api/v1/identity/authentication-events", cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var failedLoginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest("admin", "WrongPassword!2026")),
        };
        failedLoginRequest.Headers.Add("Origin", "http://localhost");
        using var failedLoginResponse = await client.SendAsync(failedLoginRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, failedLoginResponse.StatusCode);

        var token = await IntegrationTestAuthHelper.LoginAsHostUserAsync(
            client, "admin", FullNetApiFactory.TestPassword, cancellationToken);
        var ordinaryUsername = $"authentication-event-reader-{Guid.NewGuid():N}";
        using (var createUser = new HttpRequestMessage(HttpMethod.Post, "/api/v1/identity/users")
        {
            Content = JsonContent.Create(new CreateHostUserRequest(
                ordinaryUsername, "Authentication event permission test", FullNetApiFactory.TestPassword)),
        })
        {
            createUser.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var created = await client.SendAsync(createUser, cancellationToken);
            Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        }
        using var ordinaryClient = factory.CreateClientForHost("localhost");
        var ordinaryToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(
            ordinaryClient, ordinaryUsername, FullNetApiFactory.TestPassword, cancellationToken);
        using var ordinaryList = new HttpRequestMessage(HttpMethod.Get,
            "/api/v1/identity/authentication-events");
        ordinaryList.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ordinaryToken);
        using var ordinaryListResponse = await ordinaryClient.SendAsync(ordinaryList, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, ordinaryListResponse.StatusCode);
        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get, "/api/v1/identity/authentication-events?page=1&pageSize=20&eventType=login");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var page = await listResponse.Content.ReadFromJsonAsync<
            AuthenticationEventCursorPage>(cancellationToken);
        Assert.IsNotNull(page);
        var login = page.Items.FirstOrDefault(item =>
            item.EventType == "login" && item.Succeeded);
        Assert.IsNotNull(login, "登录成功的认证事件应可在管理端查询。");

        using var firstCursorRequest = new HttpRequestMessage(HttpMethod.Get,
            "/api/v1/identity/authentication-events?pageSize=1&eventType=login");
        firstCursorRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var firstCursorResponse = await client.SendAsync(firstCursorRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, firstCursorResponse.StatusCode);
        using var firstCursorBody = JsonDocument.Parse(
            await firstCursorResponse.Content.ReadAsStringAsync(cancellationToken));
        var firstCursorId = firstCursorBody.RootElement.GetProperty("items")[0].GetProperty("id").GetString();
        var nextCursor = firstCursorBody.RootElement.GetProperty("nextCursor").GetString();
        Assert.IsFalse(string.IsNullOrWhiteSpace(nextCursor));
        using var secondCursorRequest = new HttpRequestMessage(HttpMethod.Get,
            $"/api/v1/identity/authentication-events?pageSize=1&eventType=login&cursor={Uri.EscapeDataString(nextCursor)}");
        secondCursorRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var secondCursorResponse = await client.SendAsync(secondCursorRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, secondCursorResponse.StatusCode);
        using var secondCursorBody = JsonDocument.Parse(
            await secondCursorResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreNotEqual(firstCursorId,
            secondCursorBody.RootElement.GetProperty("items")[0].GetProperty("id").GetString());
        using var mismatchedCursorRequest = new HttpRequestMessage(HttpMethod.Get,
            $"/api/v1/identity/authentication-events?pageSize=1&eventType=logout&cursor={Uri.EscapeDataString(nextCursor)}");
        mismatchedCursorRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var mismatchedCursorResponse = await client.SendAsync(mismatchedCursorRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, mismatchedCursorResponse.StatusCode);
        using var invalidCursorRequest = new HttpRequestMessage(HttpMethod.Get,
            "/api/v1/identity/authentication-events?pageSize=1&cursor=invalid");
        invalidCursorRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var invalidCursorResponse = await client.SendAsync(invalidCursorRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, invalidCursorResponse.StatusCode);

        var tiedAt = DateTimeOffset.UtcNow.AddSeconds(-2);
        var tiedIds = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
            tenant.SetHost();
            try
            {
                var commands = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
                foreach (var id in tiedIds)
                {
                    Assert.AreEqual(1, await commands.ExecuteAsync(
                        IdentitySql.InsertAuthAudit,
                        new AuthAuditEvent(id, null, null, new string('0', 64),
                            "integration.cursor", "integration.cursor", true,
                            null, null, null, tiedAt), cancellationToken));
                }
            }
            finally
            {
                tenant.Clear();
            }
        }

        using var tiedFirstRequest = new HttpRequestMessage(HttpMethod.Get,
            "/api/v1/identity/authentication-events?pageSize=1&eventType=integration.cursor");
        tiedFirstRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var tiedFirstResponse = await client.SendAsync(tiedFirstRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, tiedFirstResponse.StatusCode);
        var tiedFirst = await tiedFirstResponse.Content.ReadFromJsonAsync<AuthenticationEventCursorPage>(cancellationToken);
        Assert.IsNotNull(tiedFirst);
        Assert.HasCount(1, tiedFirst.Items);
        Assert.IsFalse(string.IsNullOrWhiteSpace(tiedFirst.NextCursor));
        using var tiedSecondRequest = new HttpRequestMessage(HttpMethod.Get,
            $"/api/v1/identity/authentication-events?pageSize=1&eventType=integration.cursor&cursor={Uri.EscapeDataString(tiedFirst.NextCursor)}");
        tiedSecondRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var tiedSecondResponse = await client.SendAsync(tiedSecondRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, tiedSecondResponse.StatusCode);
        var tiedSecond = await tiedSecondResponse.Content.ReadFromJsonAsync<AuthenticationEventCursorPage>(cancellationToken);
        Assert.IsNotNull(tiedSecond);
        Assert.HasCount(1, tiedSecond.Items);
        CollectionAssert.AreEquivalent(tiedIds, new[] { tiedFirst.Items[0].Id, tiedSecond.Items[0].Id });

        using var failedEventRequest = new HttpRequestMessage(HttpMethod.Get,
            "/api/v1/identity/authentication-events?eventType=login&succeeded=false");
        failedEventRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var failedEventResponse = await client.SendAsync(failedEventRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, failedEventResponse.StatusCode);
        var failedEvents = await failedEventResponse.Content.ReadFromJsonAsync<
            AuthenticationEventCursorPage>(cancellationToken);
        Assert.IsNotNull(failedEvents);
        Assert.IsTrue(failedEvents.Items.Any(item =>
            item.EventType == "login" && !item.Succeeded && item.ActorUserId is null));

        using var detailRequest = new HttpRequestMessage(HttpMethod.Get,
            $"/api/v1/identity/authentication-events/{login.Id:D}");
        detailRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var detailResponse = await client.SendAsync(detailRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, detailResponse.StatusCode);
        var body = await detailResponse.Content.ReadAsStringAsync(cancellationToken);
        using var parsed = JsonDocument.Parse(body);
        Assert.AreEqual(login.Id.ToString("D"),
            parsed.RootElement.GetProperty("id").GetString());
        Assert.IsFalse(parsed.RootElement.TryGetProperty("usernameFingerprint", out _));
        Assert.IsFalse(parsed.RootElement.TryGetProperty("ipAddress", out _));
        Assert.IsFalse(parsed.RootElement.TryGetProperty("userAgent", out _));

        using var missingRequest = new HttpRequestMessage(HttpMethod.Get,
            $"/api/v1/identity/authentication-events/{Guid.NewGuid():D}");
        missingRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var missingResponse = await client.SendAsync(missingRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, missingResponse.StatusCode);
        Assert.AreEqual("application/problem+json",
            missingResponse.Content.Headers.ContentType?.MediaType);

        using var invalidRequest = new HttpRequestMessage(HttpMethod.Get,
            "/api/v1/identity/authentication-events?pageSize=101");
        invalidRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var invalidResponse = await client.SendAsync(invalidRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, invalidResponse.StatusCode);

        using var excessivePageRequest = new HttpRequestMessage(HttpMethod.Get,
            "/api/v1/identity/authentication-events?page=501");
        excessivePageRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var excessivePageResponse = await client.SendAsync(excessivePageRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, excessivePageResponse.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
            tenant.SetHost();
            try
            {
                var formulaAudit = new AuthAuditEvent(Guid.CreateVersion7(), null, null,
                    new string('0', 64), "=SUM(1+1)", "integration.csv", true,
                    null, null, null, DateTimeOffset.UtcNow);
                Assert.AreEqual(1, await scope.ServiceProvider.GetRequiredService<ICommandExecutor>()
                    .ExecuteAsync(IdentitySql.InsertAuthAudit, formulaAudit, cancellationToken));
            }
            finally
            {
                tenant.Clear();
            }
        }

        var now = DateTimeOffset.UtcNow;
        var exportPath = $"/api/v1/identity/authentication-events/exports?fromUtc={Uri.EscapeDataString(now.AddDays(-1).ToString("O"))}&toUtc={Uri.EscapeDataString(now.AddMinutes(1).ToString("O"))}";
        using var anonymousExport = await client.GetAsync(exportPath, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymousExport.StatusCode);
        using var ordinaryExport = new HttpRequestMessage(HttpMethod.Get, exportPath);
        ordinaryExport.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ordinaryToken);
        using var ordinaryExportResponse = await ordinaryClient.SendAsync(ordinaryExport, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, ordinaryExportResponse.StatusCode);
        using var exportRequest = new HttpRequestMessage(HttpMethod.Get, exportPath);
        exportRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var exportResponse = await client.SendAsync(exportRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, exportResponse.StatusCode);
        var csv = await exportResponse.Content.ReadAsStringAsync(cancellationToken);
        StringAssert.Contains(csv, "\"'=SUM(1+1)\"");
        Assert.IsFalse(csv.Contains("UsernameFingerprint", StringComparison.Ordinal));

        using var exportAuditRequest = new HttpRequestMessage(HttpMethod.Get,
            "/api/v1/identity/authentication-events?eventType=authentication_events.export");
        exportAuditRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var exportAuditResponse = await client.SendAsync(exportAuditRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, exportAuditResponse.StatusCode);
        var exportEvents = await exportAuditResponse.Content.ReadFromJsonAsync<
            AuthenticationEventCursorPage>(cancellationToken);
        Assert.IsNotNull(exportEvents);
        Assert.IsTrue(exportEvents.Items.Any(item => item.EventType == "authentication_events.export"));
    }
}
