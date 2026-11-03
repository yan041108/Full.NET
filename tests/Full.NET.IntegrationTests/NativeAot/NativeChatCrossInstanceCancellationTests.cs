using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>两实际 API 进程共享权威数据库，验证远端取消不让原代完成或重复计费。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeChatCrossInstanceCancellationTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Cross_instance_cancellation_stops_original_generation(DatabaseProvider provider)
    {
        var artifact = NativeApiArtifactLocator.RequireArtifact();
        await using var modelServer = await NativeChatRaceModelServer.StartAsync();
        var connection = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var stamp = Guid.CreateVersion7().ToString("N");
        var settings = new Dictionary<string, string?>
        {
            ["Identity:SessionLoginPolicy"] = "AllowMultiple",
            ["Files:Local:RootPath"] = Path.Combine(Path.GetTempPath(), "fullnet-a06-native-chat-files", stamp),
            ["DataProtection:KeyRingPath"] = Path.Combine(Path.GetTempPath(), "fullnet-a06-native-chat-keys", stamp),
            ["CodeGeneration:Artifacts:RootDirectory"] = Path.Combine(Path.GetTempPath(), "fullnet-a06-native-chat-codegen", stamp),
        };
        Directory.CreateDirectory(settings["CodeGeneration:Artifacts:RootDirectory"]!);
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
        await db.OpenAsync();
        var registry = Environment.GetEnvironmentVariable("FULLNET_A06_CHAT_NATIVE_DATABASE_REGISTRY")
            ?? throw new InvalidOperationException("必须准确登记原生验收数据库。");
        await File.AppendAllTextAsync(registry, JsonSerializer.Serialize(new { provider = provider.ToString(), database = db.Database,
            filesRoot = settings["Files:Local:RootPath"], keysRoot = settings["DataProtection:KeyRingPath"], codegenRoot = settings["CodeGeneration:Artifacts:RootDirectory"] }) + "\n");
        using var factory = new FullNetApiFactory(provider, connection, settings);
        await factory.InitializeAsync();
        var actorName = "a06-chat-" + stamp;
        var actor = await factory.CreateHostIdentityAsync(actorName,
            [AiModelPermissions.Read, AiChatPermissions.Read, AiChatPermissions.Create, AiChatPermissions.Send,
                AiChatPermissions.Cancel, "tenancy.tenants.switch"], password: FullNetApiFactory.TestPassword);
        await factory.EnsureHostUserProfileEmailAsync(actor.UserId, actorName + "@example.test", actorName);
        await using var primary = await NativeApiProcessHost.StartAsync(artifact, provider, connection, settings, NativeAotTestTimeouts.ProcessStartup);
        await using var secondary = await NativeApiProcessHost.StartAsync(artifact, provider, connection, settings, NativeAotTestTimeouts.ProcessStartup);
        using var administrator = primary.CreateClient();
        using var writer = primary.CreateClient();
        using var canceller = secondary.CreateClient();
        administrator.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await NativeApiE2EAssertions.LoginAsync(administrator, primary.LogFilePath));
        writer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(writer, actorName));
        canceller.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(canceller, actorName));
        using var created = await administrator.PostAsJsonAsync("/api/v1/ai/model-configs", new CreateAiModelConfigRequest(null,
            "a06 controlled", "openai_compatible", "https://api.openai.com/v1", "gpt-4.1-mini", "native-test-placeholder", null, false, true));
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
        var model = (await created.Content.ReadFromJsonAsync<AiModelConfigResponse>())!;
        await RunAsync(null);
        using (var available = await administrator.GetAsync("/api/v1/tenancy/available"))
        {
            Assert.AreEqual(HttpStatusCode.OK, available.StatusCode);
            using var data = JsonDocument.Parse(await available.Content.ReadAsStringAsync());
            var tenant = data.RootElement.EnumerateArray().Single(row => row.GetProperty("identifier").GetString() == "acme").GetProperty("id").GetGuid();
            var adminTenant = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(administrator,
                administrator.DefaultRequestHeaders.Authorization!.Parameter!, tenant);
            administrator.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminTenant);
            using var invitationResponse = await administrator.PostAsJsonAsync("/api/v1/identity/tenant-members/invitations",
                new CreateTenantInvitationRequest(actorName + "@example.test", TenantMemberRoles.Admin));
            Assert.AreEqual(HttpStatusCode.OK, invitationResponse.StatusCode, await invitationResponse.Content.ReadAsStringAsync());
            var invitation = (await invitationResponse.Content.ReadFromJsonAsync<CreateTenantInvitationResult>())!;
            using var accepted = await writer.PostAsJsonAsync("/api/v1/identity/tenant-invitations/accept", new AcceptTenantInvitationRequest(invitation.InvitationToken));
            Assert.AreEqual(HttpStatusCode.OK, accepted.StatusCode, await accepted.Content.ReadAsStringAsync());
            foreach (var client in new[] { writer, canceller })
            {
                var hostToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, actorName);
                var token = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(client, hostToken, tenant);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
            // 聊天创建/发送/取消目前明确为 Host 能力；Tenant 会员不能因测试而获得这些权限。
            var callsBefore = modelServer.Calls;
            using var denied = await writer.PostAsJsonAsync("/api/v1/ai/chat/sessions", new CreateAiChatSessionRequest(model.Id, "tenant denied"));
            Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode, await denied.Content.ReadAsStringAsync());
            Assert.AreEqual(callsBefore, modelServer.Calls, "Tenant 权限拒绝不得派发模型。");
        }
        using var crashWriter = primary.CreateClient();
        using var crashCanceller = secondary.CreateClient();
        foreach (var client in new[] { crashWriter, crashCanceller })
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, actorName));
        await CrashAndRecoverAsync(crashWriter, crashCanceller);

        async Task CrashAndRecoverAsync(HttpClient crashWriter, HttpClient crashCanceller)
        {
            using var createdSession = await crashWriter.PostAsJsonAsync("/api/v1/ai/chat/sessions", new CreateAiChatSessionRequest(model.Id, "a06 crash"));
            Assert.AreEqual(HttpStatusCode.Created, createdSession.StatusCode, await createdSession.Content.ReadAsStringAsync());
            var session = (await createdSession.Content.ReadFromJsonAsync<AiChatSessionResponse>())!;
            modelServer.Next();
            var before = modelServer.Calls;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(100));
            using var originalRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/ai/chat/sessions/{session.Id}/messages/stream")
            { Content = JsonContent.Create(new StreamAiChatMessageRequest("controlled crash before receipt")) };
            originalRequest.Headers.Accept.ParseAdd("text/event-stream");
            using var originalStream = await crashWriter.SendAsync(originalRequest, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            Assert.AreEqual(HttpStatusCode.OK, originalStream.StatusCode);
            await modelServer.Entered.WaitAsync(deadline.Token);
            var original = await db.QuerySingleAsync<Guid>("SELECT GenerationId FROM fn_ai_chat_session WHERE Id=@Id", new { Id = session.Id });
            await primary.CrashAsync(deadline.Token);
            Assert.IsNotNull(primary.ExitCode, "仅本夹具启动的原生进程必须真实退出。");
            var prior = await db.QuerySingleAsync<(string UsageStatus, long ReservedTokens, long ChargedTokens)>(
                "SELECT UsageStatus,ReservedTokens,ChargedTokens FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = original });
            Assert.AreEqual("reserved", prior.UsageStatus, "崩溃不能伪造未知用量的结算回执。");
            Assert.AreEqual(prior.ReservedTokens, prior.ChargedTokens);
            while (await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_chat_session WHERE Id=@Id AND GenerationId=@Generation AND IsGenerating=1 AND GenerationExpiresAtUtc>@Now",
                new { Id = session.Id, Generation = original, Now = DateTime.UtcNow }) != 0)
                await Task.Delay(TimeSpan.FromSeconds(1), deadline.Token);
            Assert.AreEqual(before + 1, modelServer.Calls, "原请求崩溃与租约到期不得自动重派。");
            modelServer.Next();
            using var explicitRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/ai/chat/sessions/{session.Id}/messages/stream")
            { Content = JsonContent.Create(new StreamAiChatMessageRequest("explicit new user request")) };
            explicitRequest.Headers.Accept.ParseAdd("text/event-stream");
            using var recovered = await crashCanceller.SendAsync(explicitRequest, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            Assert.AreEqual(HttpStatusCode.OK, recovered.StatusCode);
            await modelServer.Entered.WaitAsync(deadline.Token);
            var replacement = await db.QuerySingleAsync<Guid>("SELECT GenerationId FROM fn_ai_chat_session WHERE Id=@Id", new { Id = session.Id });
            Assert.AreNotEqual(original, replacement, "显式新请求必须拥有新代次与新操作身份。");
            Assert.AreEqual("failed", await db.QuerySingleAsync<string>("SELECT StatusKey FROM fn_ai_chat_message WHERE Id=@Id", new { Id = original }));
            modelServer.Resume();
            var body = await recovered.Content.ReadAsStringAsync(deadline.Token);
            Assert.Contains("event: done", body, StringComparison.OrdinalIgnoreCase, "显式新请求必须正常完成。");
            Assert.AreEqual(before + 2, modelServer.Calls, "只允许原请求与用户显式新请求各派发一次。");
            var after = await db.QuerySingleAsync<(string UsageStatus, long ReservedTokens, long ChargedTokens)>(
                "SELECT UsageStatus,ReservedTokens,ChargedTokens FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = original });
            Assert.AreEqual(prior, after, "新请求不能改写原未知操作的预算预留。");
            Assert.AreEqual(1, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = original }));
            Console.WriteLine($"A06 crash recovery: {provider}, crashedPid={primary.ProcessId}, survivorPid={secondary.ProcessId}, original operation retained, no automatic dispatch");
        }

        async Task RunAsync(Guid? tenant)
        {
            using var sessionResponse = await writer.PostAsJsonAsync("/api/v1/ai/chat/sessions", new CreateAiChatSessionRequest(model.Id, "a06 cancellation"));
            Assert.AreEqual(HttpStatusCode.Created, sessionResponse.StatusCode, await sessionResponse.Content.ReadAsStringAsync());
            var session = (await sessionResponse.Content.ReadFromJsonAsync<AiChatSessionResponse>())!;
            var callsBefore = modelServer.Calls;
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/ai/chat/sessions/{session.Id}/messages/stream")
            { Content = JsonContent.Create(new StreamAiChatMessageRequest("controlled cross-instance cancellation")) };
            request.Headers.Accept.ParseAdd("text/event-stream");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(50));
            using var stream = await writer.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            Assert.AreEqual(HttpStatusCode.OK, stream.StatusCode);
            await modelServer.Entered.WaitAsync(deadline.Token);
            var generation = await db.QuerySingleAsync<Guid>("SELECT GenerationId FROM fn_ai_chat_session WHERE Id=@Id", new { Id = session.Id });
            using var cancel = await canceller.PostAsync($"/api/v1/ai/chat/sessions/{session.Id}/cancel", null, deadline.Token);
            Assert.AreEqual(HttpStatusCode.OK, cancel.StatusCode, await cancel.Content.ReadAsStringAsync(deadline.Token));
            modelServer.Resume();
            var body = await stream.Content.ReadAsStringAsync(deadline.Token);
            Assert.IsFalse(body.Contains("event: done", StringComparison.OrdinalIgnoreCase), "远端取消后不能报告成功终态。");
            Assert.Contains("event: error", body, StringComparison.OrdinalIgnoreCase, "已开始的流必须发送安全错误终态。");
            Assert.AreEqual(callsBefore + 1, modelServer.Calls, "取消与恢复不得重派模型。");
            var row = await db.QuerySingleAsync<(string StatusKey, string Content)>(
                "SELECT StatusKey,Content FROM fn_ai_chat_message WHERE Id=@Id", new { Id = generation });
            Assert.AreEqual("cancelled", row.StatusKey, "取消后的原代消息必须收敛为取消状态。");
            Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_chat_session WHERE Id=@Id AND (IsGenerating<>0 OR GenerationId IS NOT NULL)", new { Id = session.Id }));
            var budget = await db.QuerySingleAsync<(string UsageStatus, long ReservedTokens, long ChargedTokens)>(
                "SELECT UsageStatus,ReservedTokens,ChargedTokens FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = generation });
            Assert.AreEqual("unknown", budget.UsageStatus, "没有收到用量回执时保持未知。");
            Assert.AreEqual(budget.ReservedTokens, budget.ChargedTokens, "未知用量保守保留原预留。");
            Assert.AreEqual(1, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = generation }));
            Console.WriteLine($"A06 cross-process cancellation: {provider}, tenant={tenant.HasValue}, calls=1, usage=unknown");
        }
    }
}
