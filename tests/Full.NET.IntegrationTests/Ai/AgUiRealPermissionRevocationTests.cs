using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.IntegrationTests.NativeAot;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Calendar.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实角色授权在 SSE 长连接中撤销后立即拒绝后续读取。</summary>
[TestClass][DoNotParallelize]
public sealed class AgUiRealPermissionRevocationTests
{
    [TestMethod][DataRow(DatabaseProvider.SqlServer, true)][DataRow(DatabaseProvider.MySql, true)]
    [DataRow(DatabaseProvider.SqlServer, false)][DataRow(DatabaseProvider.MySql, false)]
    public Task Revoking_the_owner_permission_ends_the_existing_stream(DatabaseProvider provider, bool formal) => VerifyAsync(provider, formal, false);

    internal static async Task VerifyAsync(DatabaseProvider provider, bool formal, bool native)
    {
        var connection = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var settings = AiAgentRuntimeTestSettings.ForIntegrationTests();
        settings["Tenancy:HostDomains:1"] = "127.0.0.1";
        var stamp = Guid.CreateVersion7().ToString("N");
        settings["Files:Local:RootPath"] = Path.Combine(Path.GetTempPath(), "fullnet-a04-revoke-files", stamp);
        settings["DataProtection:KeyRingPath"] = Path.Combine(Path.GetTempPath(), "fullnet-a04-revoke-keys", stamp);
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
        await db.OpenAsync();
        var registry = Environment.GetEnvironmentVariable("FULLNET_A04_REVOKE_DATABASE_REGISTRY")
            ?? throw new InvalidOperationException("必须准确登记本批数据库。");
        await File.AppendAllTextAsync(registry, JsonSerializer.Serialize(new
        { provider = provider.ToString(), database = db.Database, filesRoot = settings["Files:Local:RootPath"], keysRoot = settings["DataProtection:KeyRingPath"] }) + "\n");
        using var factory = new FullNetApiFactory(provider, connection, settings);
        if (!native) factory.UseKestrel(options => options.ListenAnyIP(64215));
        await factory.InitializeAsync();
        await using var api = native ? await NativeApiProcessHost.StartAsync(NativeApiArtifactLocator.RequireArtifact(), provider,
            connection, settings, NativeAotTestTimeouts.ProcessStartup, listenAddress: new Uri("http://0.0.0.0:64215/")) : null;
        using var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:64215"), Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.Host = "localhost";
        var admin = await NativeApiE2EAssertions.LoginAsync(client, "A04 revocation Kestrel");
        string[] permissions = [CalendarPermissions.Read, CalendarPermissions.Create, CalendarPermissions.SetStatus,
            PersonalScheduleCompletionKeys.Permission, AiAgentRunPermissions.Read, AiAgentRunPermissions.Create, AiModelPermissions.Read];
        var owner = await factory.CreateHostIdentityAsync("agui-revoke-" + stamp, permissions,
            password: native ? FullNetApiFactory.TestPassword : null);
        // Native 与 JIT 测试宿主使用各自的临时签名环；Native 须经真实登录获取本宿主签发的令牌。
        var ownerToken = native ? await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, owner.Username) : owner.AccessToken;
        var model = await Send<AiModelConfigResponse>(HttpMethod.Post, "/api/v1/ai/model-configs", admin,
            new CreateAiModelConfigRequest(null, "撤权验证元数据", "ollama", "https://provider.test", "metadata", null, null, false, true), HttpStatusCode.Created);
        var now = DateTimeOffset.UtcNow;
        var schedule = await Send<PersonalScheduleResponse>(HttpMethod.Post, "/api/v1/calendar/my-personal-schedules", ownerToken,
            new CreatePersonalScheduleRequest("PRIVATE_REVOKE_TARGET", now, now.AddMinutes(10)), HttpStatusCode.Created);
        await AiAgentRunApiAssertions.SeedFreshWorkerHeartbeatAsync(factory);
        var run = (await Send<CreateAiAgentRunResponse>(HttpMethod.Post, "/api/v1/ai/agent/runs", ownerToken,
            new CreateAiAgentRunRequest(Guid.CreateVersion7(), "fullnet-personal-schedule-complete-v1", model.Id, "", 1, 1,
                PersonalScheduleTarget: new(schedule.Id, schedule.Version)), HttpStatusCode.Accepted)).RunId;
        var roleId = await db.QuerySingleAsync<Guid>("SELECT RoleId FROM fn_identity_user_role WHERE UserId=@Id", new { Id = owner.UserId });
        var eventCount = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_agent_event WHERE RunId=@Id", new { Id = run });
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/ai/agent/runs/{run}/events/stream");
        request.Headers.Authorization = new("Bearer", ownerToken);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var stream = new StreamReader(await response.Content.ReadAsStreamAsync(deadline.Token));
        var snapshot = false;
        while (await stream.ReadLineAsync(deadline.Token) is { } line)
            if (line == "event: STATE_SNAPSHOT") { snapshot = true; break; }
        Assert.IsTrue(snapshot, "权限撤销前必须已交付快照。");
        if (formal)
        {
            // 正式角色变更主动撤销成员会话，动作权限也不能独立于页面权限保留。
            var role = await Send<HostRoleResponse>(HttpMethod.Put, $"/api/v1/identity/roles/{roleId:D}/permissions", admin,
                new ReplaceHostRolePermissionsRequest([CalendarPermissions.Read], 1), HttpStatusCode.OK);
            Assert.IsFalse(role.PermissionCodes.Contains(AiAgentRunPermissions.Read));
        }
        else
        {
            // 独立数据库夹具只改变权威权限行，用于隔离持续权限检查；不冒充正式角色更新。
            Assert.AreEqual(1, await db.ExecuteAsync("DELETE FROM fn_identity_role_permission WHERE RoleId=@RoleId AND PermissionCode=@Permission",
                new { RoleId = roleId, Permission = AiAgentRunPermissions.Read }));
        }
        using (var check = new HttpRequestMessage(HttpMethod.Get, "/api/v1/calendar/my-personal-schedules"))
        {
            check.Headers.Authorization = new("Bearer", ownerToken);
            using var checkResponse = await client.SendAsync(check, deadline.Token);
            Assert.AreEqual(formal ? HttpStatusCode.Unauthorized : HttpStatusCode.OK, checkResponse.StatusCode,
                "正式角色变更撤销会话；单独权限夹具保留会话及日程读取。");
        }
        var denied = false;
        while (await stream.ReadLineAsync(deadline.Token) is { } line)
            if (line.StartsWith("data: ", StringComparison.Ordinal) && line.Contains("authorization.permission_denied", StringComparison.Ordinal))
            { denied = true; break; }
        Assert.IsTrue(denied, "撤权后必须结束原流并返回安全拒绝码。");
        Assert.AreEqual(eventCount, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_agent_event WHERE RunId=@Id", new { Id = run }));
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_schedule_completion_intent"));
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget"));
        Console.WriteLine($"Role permission revoked on active TCP stream: provider={provider}, formal={formal}, native={native}; no tool or model dispatch.");

        async Task<T> Send<T>(HttpMethod method, string path, string token, object? body, HttpStatusCode expected)
        {
            using var message = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
            message.Headers.Authorization = new("Bearer", token);
            using var result = await client.SendAsync(message);
            Assert.AreEqual(expected, result.StatusCode, await result.Content.ReadAsStringAsync());
            return (await result.Content.ReadFromJsonAsync<T>())!;
        }
    }
}

/// <summary>正式 Native API 验证角色更新和独立权限变化。</summary>
[TestClass][DoNotParallelize]
public sealed class NativeAgUiRealPermissionRevocationTests
{
    [TestMethod][DataRow(DatabaseProvider.SqlServer, true)][DataRow(DatabaseProvider.MySql, true)]
    [DataRow(DatabaseProvider.SqlServer, false)][DataRow(DatabaseProvider.MySql, false)]
    public Task Revoking_the_owner_permission_ends_the_existing_stream(DatabaseProvider provider, bool formal) =>
        AgUiRealPermissionRevocationTests.VerifyAsync(provider, formal, true);
}
