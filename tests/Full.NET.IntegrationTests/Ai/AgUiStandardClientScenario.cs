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
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>正式JWT和真实数据库建立静态Run；独立标准客户端通过实际TCP消费，只读协议不驱动Worker。</summary>
internal static class AgUiStandardClientScenario
{
    internal static async Task VerifyAsync(DatabaseProvider provider, bool native)
    {
        var root = Environment.GetEnvironmentVariable("FULLNET_A04_AGUI_BRIDGE")
            ?? throw new InvalidOperationException("必须指定全新A04标准客户端桥接目录。");
        var bridge = Path.Combine(root, provider.ToString());
        if (Directory.Exists(bridge)) throw new InvalidOperationException("不得采用已有桥接结果。");
        Directory.CreateDirectory(bridge);
        var connection = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var stamp = Guid.CreateVersion7().ToString("N");
        var settings = AiAgentRuntimeTestSettings.ForIntegrationTests();
        // 外部Node使用Docker发布的回环地址；它是本夹具明确批准的Host别名，不修改生产域名或租户策略。
        settings["Tenancy:HostDomains:1"] = "127.0.0.1";
        settings["Files:Local:RootPath"] = Path.Combine(Path.GetTempPath(), "fullnet-a04-agui-files", stamp);
        settings["DataProtection:KeyRingPath"] = Path.Combine(Path.GetTempPath(), "fullnet-a04-agui-keys", stamp);
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
        await db.OpenAsync();
        var registry = Environment.GetEnvironmentVariable("FULLNET_A04_DATABASE_REGISTRY")
            ?? throw new InvalidOperationException("必须准确登记本批数据库。");
        await File.AppendAllTextAsync(registry, JsonSerializer.Serialize(new
        { provider = provider.ToString(), database = db.Database, native, filesRoot = settings["Files:Local:RootPath"], keysRoot = settings["DataProtection:KeyRingPath"] }) + "\n");
        using var factory = new FullNetApiFactory(provider, connection, settings);
        // .NET 10正式测试宿主支持Kestrel，避免把TestServer内存Handler当标准客户端网络证据。
        if (!native) factory.UseKestrel(options => options.ListenAnyIP(64215));
        await factory.InitializeAsync();
        await using var api = native ? await NativeApiProcessHost.StartAsync(NativeApiArtifactLocator.RequireArtifact(), provider,
            connection, settings, NativeAotTestTimeouts.ProcessStartup, listenAddress: new Uri("http://0.0.0.0:64215/")) : null;
        using var adminClient = Client();
        using var ownerClient = Client();
        using var otherClient = Client();
        var admin = await NativeApiE2EAssertions.LoginAsync(adminClient, api?.LogFilePath ?? "A04 Kestrel");
        string[] permissions = [CalendarPermissions.Read, CalendarPermissions.Create, CalendarPermissions.SetStatus,
            PersonalScheduleCompletionKeys.Permission, AiAgentRunPermissions.Read, AiAgentRunPermissions.Create,
            AiAgentRunPermissions.Cancel, AiModelPermissions.Read, "tenancy.tenants.switch"];
        var ownerName = "agui-owner-" + stamp;
        var otherName = "agui-other-" + stamp;
        var ownerIdentity = await factory.CreateHostIdentityAsync(ownerName, permissions, password: FullNetApiFactory.TestPassword);
        var otherIdentity = await factory.CreateHostIdentityAsync(otherName, permissions, password: FullNetApiFactory.TestPassword);
        await factory.EnsureHostUserProfileEmailAsync(ownerIdentity.UserId, ownerName + "@example.test", ownerName);
        await factory.EnsureHostUserProfileEmailAsync(otherIdentity.UserId, otherName + "@example.test", otherName);
        var owner = await IntegrationTestAuthHelper.LoginAsHostUserAsync(ownerClient, ownerName);
        var other = await IntegrationTestAuthHelper.LoginAsHostUserAsync(otherClient, otherName);
        var model = await Send<AiModelConfigResponse>(HttpMethod.Post, "/api/v1/ai/model-configs", admin,
            new CreateAiModelConfigRequest(null, "协议静态图元数据", "ollama", "https://provider.test", "metadata", null, null, false, true), HttpStatusCode.Created);
        var tenant = (await Send<TenantContextSummary[]>(HttpMethod.Get, "/api/v1/tenancy/available", admin)).Single(x => x.Identifier == "acme");
        Guid hostRun = Guid.Empty;
        foreach (var tenantScope in new[] { false, true })
        {
            if (tenantScope)
            {
                var tenantAdmin = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(adminClient, admin, tenant.Id);
                foreach (var person in new[] { (name: ownerName, token: owner), (name: otherName, token: other) })
                {
                    var invite = await Send<CreateTenantInvitationResult>(HttpMethod.Post, "/api/v1/identity/tenant-members/invitations", tenantAdmin,
                        new CreateTenantInvitationRequest(person.name + "@example.test", TenantMemberRoles.Admin));
                    await Send<AcceptTenantInvitationResponse>(HttpMethod.Post, "/api/v1/identity/tenant-invitations/accept", person.token,
                        new AcceptTenantInvitationRequest(invite.InvitationToken));
                }
                owner = await IntegrationTestAuthHelper.LoginAsHostUserAsync(ownerClient, ownerName);
                other = await IntegrationTestAuthHelper.LoginAsHostUserAsync(otherClient, otherName);
                owner = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(ownerClient, owner, tenant.Id);
                other = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(otherClient, other, tenant.Id);
            }
            var scope = tenantScope ? tenant.Id.ToString("N") : "host";
            await AiAgentRunApiAssertions.SeedFreshWorkerHeartbeatAsync(factory);
            var terminal = await CreateRun();
            await Send<bool>(HttpMethod.Post, $"/api/v1/ai/agent/runs/{terminal}/cancel", owner);
            if (!tenantScope) hostRun = terminal;
            var active = await CreateRun();
            // 大历史仅是协议载荷夹具；实际Run由公开API创建，未运行任何模型或工具，不冒充容量证明。
            var maximum = await db.QuerySingleAsync<long>("SELECT COALESCE(MAX(Sequence),0) FROM fn_ai_agent_event WHERE RunId=@Id", new { Id = terminal });
            for (var sequence = maximum + 1; sequence <= 205; sequence++)
                await db.ExecuteAsync("INSERT INTO fn_ai_agent_event (Id,RunId,Sequence,EventType,PayloadVersion,Payload,CreatedAtUtc) VALUES (@Id,@RunId,@Sequence,@EventType,1,@Payload,@Now)",
                    new { Id = Guid.CreateVersion7(), RunId = terminal, Sequence = sequence, EventType = "run.protocol_fixture", Payload = JsonSerializer.Serialize(new { label = new string('x', 8192) }), Now = DateTimeOffset.UtcNow });
            var terminalThread = await Thread(terminal);
            var activeThread = await Thread(active);
            var activeEvents = await Events(active);
            var originalRuns = await Count("fn_ai_agent_run");
            var scopeBridge = Path.Combine(bridge, scope); Directory.CreateDirectory(scopeBridge);
            await Write(scopeBridge, "ready", new { scopeKey = scope, actorKey = ownerIdentity.UserId, token = owner, otherToken = other,
                terminalRun = terminal, terminalThread, activeRun = active, activeThread, hostRun, maximum = 205, native });
            await Wait(scopeBridge, "revoke");
            // 正式重登撤销原同客户端会话；不伪造授权Port、principal或JWT到期时间。
            owner = await IntegrationTestAuthHelper.LoginAsHostUserAsync(ownerClient, ownerName);
            if (tenantScope) owner = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(ownerClient, owner, tenant.Id);
            await Wait(scopeBridge, "read-complete");
            Assert.AreEqual("queued", (await Send<AiAgentRunResponse>(HttpMethod.Get, $"/api/v1/ai/agent/runs/{active}", owner)).StatusKey);
            Assert.AreEqual(activeEvents, await Events(active));
            Assert.AreEqual("cancelled", (await Send<AiAgentRunResponse>(HttpMethod.Get, $"/api/v1/ai/agent/runs/{terminal}", owner)).StatusKey);
            await db.ExecuteAsync("DELETE FROM fn_ai_agent_event WHERE RunId=@Id AND Sequence=100", new { Id = terminal });
            await Write(scopeBridge, "gap", new { token = owner });
            await Wait(scopeBridge, "complete");
            Assert.AreEqual(originalRuns, await Count("fn_ai_agent_run"));
            Assert.AreEqual(0, await Count("fn_ai_schedule_completion_intent"));
            Assert.AreEqual(0, await Count("fn_calendar_schedule_completion"));
            Assert.AreEqual(0, await Count("fn_ai_operation_budget"));
            Assert.AreEqual(0, await Count("fn_files_file"));
            Console.WriteLine($"A04 real TCP standard client complete: {provider}, tenant={tenantScope}, native={native}; no Worker/model/tool dispatch.");

            async Task<Guid> CreateRun()
            {
                var now = DateTimeOffset.UtcNow;
                var schedule = await Send<PersonalScheduleResponse>(HttpMethod.Post, "/api/v1/calendar/my-personal-schedules", owner,
                    new CreatePersonalScheduleRequest("PRIVATE_AGUI_PROTOCOL_TARGET", now, now.AddMinutes(10)), HttpStatusCode.Created);
                return (await Send<CreateAiAgentRunResponse>(HttpMethod.Post, "/api/v1/ai/agent/runs", owner,
                    new CreateAiAgentRunRequest(Guid.CreateVersion7(), "fullnet-personal-schedule-complete-v1", model.Id, "", 1, 1,
                        PersonalScheduleTarget: new(schedule.Id, schedule.Version)), HttpStatusCode.Accepted)).RunId;
            }
        }

        HttpClient Client() { var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:64215"), Timeout = TimeSpan.FromSeconds(40) }; client.DefaultRequestHeaders.Host = "localhost"; return client; }
        Task<Guid> Thread(Guid run) => db.QuerySingleAsync<Guid>("SELECT SessionId FROM fn_ai_agent_run WHERE Id=@Id", new { Id = run });
        Task<int> Events(Guid run) => db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_agent_event WHERE RunId=@Id", new { Id = run });
        Task<int> Count(string table) => db.QuerySingleAsync<int>("SELECT COUNT(*) FROM " + table);
        async Task<T> Send<T>(HttpMethod method, string path, string token, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
        {
            using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
            request.Headers.Authorization = new("Bearer", token); using var response = await adminClient.SendAsync(request);
            Assert.AreEqual(expected, response.StatusCode, $"{method} {path}: {await response.Content.ReadAsStringAsync()}");
            return (await response.Content.ReadFromJsonAsync<T>())!;
        }
        static async Task Write(string directory, string name, object value)
        { var path = Path.Combine(directory, name + ".json"); await File.WriteAllTextAsync(path + ".writing", JsonSerializer.Serialize(value)); File.Move(path + ".writing", path); }
        async Task Wait(string directory, string name)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            while (!File.Exists(Path.Combine(directory, name + ".json")))
            { if (File.Exists(Path.Combine(root, "failed.json"))) Assert.Fail("标准客户端失败，保留原证据。"); await Task.Delay(100, timeout.Token); }
            using var result = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, name + ".json"), timeout.Token));
            Assert.IsTrue(result.RootElement.GetProperty("passed").GetBoolean());
        }
    }
}

/// <summary>双库JIT Kestrel公开SSE，外部官方SDK消费。</summary>
[TestClass][DoNotParallelize]
public sealed class AgUiStandardClientApiTests
{
    [TestMethod][DataRow(DatabaseProvider.SqlServer)][DataRow(DatabaseProvider.MySql)]
    public Task Standard_client_replay_and_current_session_are_read_only(DatabaseProvider provider) => AgUiStandardClientScenario.VerifyAsync(provider, false);
}
