using System.Net;
using System.Net.Http.Headers;
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

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实 Calendar 所有者契约与 AI 来源预览，模型、费用及文件对象均不参与。</summary>
internal static class PersonalScheduleSourceScenario
{
    internal static async Task VerifyAsync(DatabaseProvider provider, bool native)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var stamp = Guid.CreateVersion7().ToString("N");
        var settings = new Dictionary<string, string?>
        {
            ["Files:Local:RootPath"] = Path.Combine(Path.GetTempPath(), "fullnet-k06-source-files", stamp),
            ["DataProtection:KeyRingPath"] = Path.Combine(Path.GetTempPath(), "fullnet-k06-source-keys", stamp)
        };
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
        if (Environment.GetEnvironmentVariable("FULLNET_K06_DATABASE_REGISTRY") is { Length: > 0 } registry)
            await File.AppendAllTextAsync(registry, JsonSerializer.Serialize(new { provider = provider.ToString(), database = db.Database,
                native, filesRoot = settings["Files:Local:RootPath"], keysRoot = settings["DataProtection:KeyRingPath"] }) + "\n");
        using var factory = new FullNetApiFactory(provider, connection, settingsOverrides: settings);
        await factory.InitializeAsync();
        string[] rights = [CalendarPermissions.Read, CalendarPermissions.Create, CalendarPermissions.Update,
            CalendarPermissions.Delete, CalendarPermissions.SetStatus, PersonalScheduleKnowledgePermissions.Read];
        var ownerName = "k06-source-owner-" + stamp; var peerName = "k06-source-peer-" + stamp; var readerName = "k06-source-no-purpose-" + stamp;
        var owner = await factory.CreateHostIdentityAsync(ownerName, rights, password: FullNetApiFactory.TestPassword);
        await factory.CreateHostIdentityAsync(peerName, rights, password: FullNetApiFactory.TestPassword);
        await factory.CreateHostIdentityAsync(readerName, [CalendarPermissions.Read], password: FullNetApiFactory.TestPassword);
        await using var api = native ? await NativeApiProcessHost.StartAsync(NativeApiArtifactLocator.RequireArtifact(), provider, connection,
            settings, NativeAotTestTimeouts.ProcessStartup) : null;
        using var client = api?.CreateClient() ?? factory.CreateClientForHost("localhost");
        using var ownerClient = api?.CreateClient() ?? factory.CreateClientForHost("localhost");
        using var peerClient = api?.CreateClient() ?? factory.CreateClientForHost("localhost");
        using var readerClient = api?.CreateClient() ?? factory.CreateClientForHost("localhost");
        var admin = await NativeApiE2EAssertions.LoginAsync(client, api?.LogFilePath ?? "K06 source JIT");
        var ownerToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(ownerClient, ownerName);
        var peerToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(peerClient, peerName);
        var readerToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(readerClient, readerName);
        const string preview = "/api/v1/ai/personal-schedule-assistant/sources/preview";
        const string calendar = "/api/v1/calendar/my-personal-schedules";
        Guid? hostId = null;
        foreach (var scoped in new[] { false, true })
        {
            var actor = ownerToken;
            if (scoped)
            {
                var tenants = await Send<TenantContextSummary[]>(HttpMethod.Get, "/api/v1/tenancy/available", admin);
                actor = (await Send<TenantContextTokenResponse>(HttpMethod.Put, "/api/v1/tenancy/context", admin,
                    new ChangeTenantContextRequest(tenants.Single(value => value.Identifier == "acme").Id))).AccessToken;
            }
            var start = DateTimeOffset.UtcNow.AddHours(1);
            var first = await Send<PersonalScheduleResponse>(HttpMethod.Post, calendar, actor,
                new CreatePersonalScheduleRequest("本人来源\n<script>不执行</script>", start, start.AddHours(1)), HttpStatusCode.Created);
            var second = await Send<PersonalScheduleResponse>(HttpMethod.Post, calendar, actor,
                new CreatePersonalScheduleRequest("完整依赖中的第二项", start, start), HttpStatusCode.Created);
            var selected = new PreviewAiPersonalScheduleSources([second.Id, first.Id]);
            var result = await Send<AiPersonalScheduleSourcePreview>(HttpMethod.Post, preview, actor, selected);
            Assert.AreEqual("restricted", result.Classification); Assert.AreEqual(2, result.Sources.Count);
            CollectionAssert.AreEqual(new[] { second.Id, first.Id }, result.Sources.Select(row => row.Id).ToArray());
            Assert.AreEqual(first.Content, result.Sources[1].Content); Assert.AreEqual(first.Version, result.Sources[1].Version);
            Assert.IsTrue(result.Sources.All(row => row.Kind == PersonalScheduleKnowledgeSourceKeys.Kind
                && row.Purpose == PersonalScheduleKnowledgeSourceKeys.Purpose && row.ProjectionHash.Length == 64));
            var repeat = await Send<AiPersonalScheduleSourcePreview>(HttpMethod.Post, preview, actor, selected);
            CollectionAssert.AreEqual(result.Sources.Select(row => row.ProjectionHash).ToArray(), repeat.Sources.Select(row => row.ProjectionHash).ToArray());
            foreach (var invalid in new IReadOnlyList<Guid>[] { [], [first.Id, first.Id], [Guid.Empty], Enumerable.Range(0, 9).Select(_ => Guid.CreateVersion7()).ToArray() })
                await Expect(preview, actor, new PreviewAiPersonalScheduleSources(invalid), HttpStatusCode.BadRequest, PersonalScheduleKnowledgeErrorCodes.Invalid);
            await Expect(preview + "?tenantId=" + Guid.CreateVersion7(), actor, selected, HttpStatusCode.BadRequest, PersonalScheduleKnowledgeErrorCodes.Invalid);
            await Expect(preview, actor, new PreviewAiPersonalScheduleSources([first.Id, Guid.CreateVersion7()]), HttpStatusCode.Conflict, PersonalScheduleKnowledgeErrorCodes.Unavailable);
            await Expect(preview, actor, new { scheduleIds = new[] { first.Id }, tenantId = Guid.CreateVersion7() }, HttpStatusCode.BadRequest);
            if (!scoped)
            {
                hostId = first.Id;
                await Expect(preview, peerToken, selected, HttpStatusCode.Conflict, PersonalScheduleKnowledgeErrorCodes.Unavailable);
                await Expect(preview, readerToken, selected, HttpStatusCode.Forbidden, "authorization.permission_denied");
                var other = await Send<PersonalScheduleResponse>(HttpMethod.Post, calendar, peerToken,
                    new CreatePersonalScheduleRequest("另一所有者", start, start), HttpStatusCode.Created);
                await Expect(preview, actor, new PreviewAiPersonalScheduleSources([first.Id, other.Id]), HttpStatusCode.Conflict, PersonalScheduleKnowledgeErrorCodes.Unavailable);
            }
            else
            {
                await Expect(preview, actor, new PreviewAiPersonalScheduleSources([hostId!.Value]), HttpStatusCode.Conflict, PersonalScheduleKnowledgeErrorCodes.Unavailable);
                await Expect(preview, ownerToken, selected, HttpStatusCode.Conflict, PersonalScheduleKnowledgeErrorCodes.Unavailable);
            }
            var updated = await Send<PersonalScheduleResponse>(HttpMethod.Put, calendar + "/" + first.Id, actor,
                new UpdatePersonalScheduleRequest("准确版本已改变", first.StartAtUtc, first.EndAtUtc, first.Version));
            var changed = await Send<AiPersonalScheduleSourcePreview>(HttpMethod.Post, preview, actor, selected);
            Assert.AreEqual(updated.Version, changed.Sources[1].Version); Assert.AreNotEqual(result.Sources[1].ProjectionHash, changed.Sources[1].ProjectionHash);
            var completed = await Send<PersonalScheduleResponse>(HttpMethod.Post, calendar + "/" + first.Id + "/status", actor,
                new SetPersonalScheduleStatusRequest(PersonalScheduleStatuses.Completed, updated.Version));
            Assert.IsNotNull(completed.CompletedAtUtc);
            var completedSource = await Send<AiPersonalScheduleSourcePreview>(HttpMethod.Post, preview, actor, selected);
            Assert.AreEqual(PersonalScheduleStatuses.Completed, completedSource.Sources[1].Status);
            Assert.AreEqual(completed.Version, completedSource.Sources[1].Version);
            Assert.AreNotEqual(changed.Sources[1].ProjectionHash, completedSource.Sources[1].ProjectionHash);
            await SendStatus(HttpMethod.Post, calendar + "/" + first.Id + "/delete", actor,
                new ChangePersonalScheduleRequest(completed.Version), HttpStatusCode.NoContent);
            await Expect(preview, actor, selected, HttpStatusCode.Conflict, PersonalScheduleKnowledgeErrorCodes.Unavailable);
        }
        // 直接修改隔离测试身份夹具以验证旧JWT不能覆盖权威用途撤销；不改生产权限实现。
        await db.ExecuteAsync("DELETE FROM fn_identity_role_permission WHERE PermissionCode=@Permission AND RoleId IN (SELECT RoleId FROM fn_identity_user_role WHERE UserId=@UserId)",
            new { Permission = PersonalScheduleKnowledgePermissions.Read, owner.UserId });
        await Expect(preview, ownerToken, new PreviewAiPersonalScheduleSources([hostId!.Value]), HttpStatusCode.Forbidden, "authorization.permission_denied");
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget"));
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_files_file"));
        if (!native)
        {
            using var openapi = await client.GetAsync("/openapi/v1.json"); var json = await openapi.Content.ReadAsStringAsync();
            Assert.AreEqual(HttpStatusCode.OK, openapi.StatusCode, json);
            if (Environment.GetEnvironmentVariable("FULLNET_K06_OPENAPI_EXPORT_DIR") is { Length: > 0 } export)
            { Directory.CreateDirectory(export); await File.WriteAllTextAsync(Path.Combine(export, "source-" + provider.ToString().ToLowerInvariant() + ".json"), json); }
        }

        async Task<T> Send<T>(HttpMethod method, string path, string token, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
        {
            using var request = new HttpRequestMessage(method, path); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await client.SendAsync(request); var text = await response.Content.ReadAsStringAsync();
            Assert.AreEqual(expected, response.StatusCode, text + " NativeLog=" + api?.LogFilePath);
            if (path.Contains("sources/preview", StringComparison.Ordinal)) Assert.AreEqual("no-store", response.Headers.CacheControl?.ToString());
            return JsonSerializer.Deserialize<T>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        }
        async Task SendStatus(HttpMethod method, string path, string token, object body, HttpStatusCode expected)
        {
            using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) }; request.Headers.Authorization = new("Bearer", token);
            using var response = await client.SendAsync(request); Assert.AreEqual(expected, response.StatusCode, await response.Content.ReadAsStringAsync());
        }
        async Task Expect(string path, string token, object body, HttpStatusCode expected, string? code = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) }; request.Headers.Authorization = new("Bearer", token);
            request.Headers.AcceptLanguage.ParseAdd("en-US");
            using var response = await client.SendAsync(request); var text = await response.Content.ReadAsStringAsync();
            Assert.AreEqual(expected, response.StatusCode, text + " NativeLog=" + api?.LogFilePath);
            if (code is not null) StringAssert.Contains(text, code);
            Assert.IsFalse(text.Contains("<script>", StringComparison.Ordinal), "拒绝响应不交付来源正文。");
        }
    }
}
