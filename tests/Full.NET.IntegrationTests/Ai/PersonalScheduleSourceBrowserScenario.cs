using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.IntegrationTests.NativeAot;
using Full.NET.Modules.Calendar.Contracts;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>两个独立本人会话驱动正式日程页面；后台准备和撤回均使用正式HTTP操作。</summary>
internal static class PersonalScheduleSourceBrowserScenario
{
    internal static async Task VerifyAsync()
    {
        await using var browser = new PersonalScheduleBrowserRun("FULLNET_K06_SOURCE_BROWSER_BRIDGE");
        var bridge = browser.Bridge;
        var connection = await SharedDatabaseFixture.CreateMySqlDatabaseAsync(); var stamp = Guid.CreateVersion7().ToString("N");
        var settings = new Dictionary<string, string?>
        {
            ["Files:Local:RootPath"] = browser.OwnedDirectory is { } root
                ? Path.Combine(root, "files") : Path.Combine(Path.GetTempPath(), "fullnet-k06-source-files", stamp),
            ["DataProtection:KeyRingPath"] = browser.OwnedDirectory is { } keys
                ? Path.Combine(keys, "keys") : Path.Combine(Path.GetTempPath(), "fullnet-k06-source-keys", stamp),
            ["Identity:AllowedOrigins:1"] = browser.AdminOrigin, ["Tenancy:HostDomains:1"] = "127.0.0.1"
        };
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(false, connection);
        if (Environment.GetEnvironmentVariable("FULLNET_K06_DATABASE_REGISTRY") is { Length: > 0 } registry)
            await File.AppendAllTextAsync(registry, JsonSerializer.Serialize(new { provider = "MySql", database = db.Database,
                native = true, browser = true, filesRoot = settings["Files:Local:RootPath"], keysRoot = settings["DataProtection:KeyRingPath"] }) + "\n");
        using var factory = new FullNetApiFactory(DatabaseProvider.MySql, connection, settingsOverrides: settings); await factory.InitializeAsync();
        string[] rights = ["identity.navigation.read", "platform.dashboard.read", CalendarPermissions.Read, CalendarPermissions.Create,
            CalendarPermissions.Delete, PersonalScheduleKnowledgePermissions.Read];
        var users = new[] { "k06-source-desktop-" + stamp, "k06-source-mobile-" + stamp };
        foreach (var user in users) await factory.CreateHostIdentityAsync(user, rights, password: FullNetApiFactory.TestPassword);
        await using var api = await NativeApiProcessHost.StartAsync(NativeApiArtifactLocator.RequireArtifact(), DatabaseProvider.MySql, connection,
            settings, NativeAotTestTimeouts.ProcessStartup, listenAddress: browser.ApiListenAddress);
        using var client = api.CreateClient(); var prepared = new List<(string Username, PersonalScheduleResponse Schedule)>();
        foreach (var user in users)
        {
            using var login = api.CreateClient(); var token = await IntegrationTestAuthHelper.LoginAsHostUserAsync(login, user);
            var start = DateTimeOffset.UtcNow.AddMinutes(2);
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/calendar/my-personal-schedules")
                { Content = JsonContent.Create(new CreatePersonalScheduleRequest("本人助手来源 · <script>纯文本</script> · " + user, start, start.AddMinutes(30))) };
            request.Headers.Authorization = new("Bearer", token); using var response = await client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync(); Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, text);
            prepared.Add((user, JsonSerializer.Deserialize<PersonalScheduleResponse>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web))!));
        }
        await File.WriteAllTextAsync(Path.Combine(bridge, "ready.writing.json"), JsonSerializer.Serialize(new { password = FullNetApiFactory.TestPassword,
            users = prepared.Select(row => new { username = row.Username, scheduleId = row.Schedule.Id, content = row.Schedule.Content, version = row.Schedule.Version }).ToArray() }));
        File.Move(Path.Combine(bridge, "ready.writing.json"), Path.Combine(bridge, "ready.json"));
        browser.Start(api.BaseAddress, "playwright.personal-schedule-assistant.config.mjs", "FULLNET_K06_SOURCE_BROWSER_READY");
        await browser.WaitForMarkerAsync("advance.json", "advance", "complete.json");
        await browser.WaitForMarkerAsync("deleted-1440.json", "deleted", "complete.json");
        await browser.WaitForMarkerAsync("deleted-390.json", "deleted", "complete.json");
        // 单客户端单会话是正式安全策略；由页面当前会话调用正式删除API，后台只核对实际结果。
        foreach (var row in prepared)
            Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_calendar_personal_schedule WHERE Id=@Id", new { Id = row.Schedule.Id }));
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget"));
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_files_file"));
        await File.WriteAllTextAsync(Path.Combine(bridge, "replaced.json"), "{}");
        await browser.WaitForMarkerAsync("complete.json", "passed", "complete.json");
        await browser.RequireSuccessAsync();
    }
}
