using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>独立成熟 RP、真实 HTTPS、真实 Worker 和时钟下验证退出传播与故障恢复。</summary>
[TestClass]
[DoNotParallelize]
public sealed class IdentityOidcNetworkInteropTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Independent_rp_and_worker_preserve_logout_scope_retries_and_bounded_load(DatabaseProvider provider)
    {
        await using var fixture = new IdentityOidcNetworkFixture(provider);
        await fixture.InitializeAsync();
        using var browser = fixture.Browser(); using var otherDevice = fixture.Browser();
        await LoginAsync(fixture, browser, "a");
        await LoginAsync(fixture, browser, "b", silent: true);
        await LoginAsync(fixture, otherDevice, "a");
        await ExpectAsync(await browser.GetAsync(fixture.Rp("a", "userinfo")), HttpStatusCode.OK);
        await ExpectAsync(await browser.PostAsync(fixture.Rp("a", "refresh"), null), HttpStatusCode.OK);
        await ExpectAsync(await browser.GetAsync(fixture.Rp("a", "resource")), HttpStatusCode.OK);
        await ExpectAsync(await browser.PostAsync(fixture.Rp("a", "backchannel"), new FormUrlEncodedContent(new Dictionary<string, string> { ["logout_token"] = "invalid" })), HttpStatusCode.BadRequest);
        await ExpectAsync(await browser.GetAsync(fixture.Rp("a", "session")), HttpStatusCode.OK);
        await fixture.StartWorkerAsync();

        // 应用退出仅撤销当前应用会话；另一个 RP 与另一个设备的同客户端会话保持独立。
        using (var noBearer = new HttpRequestMessage(HttpMethod.Post, fixture.Op("/api/v1/identity/oidc/logout/application")) { Content = JsonContent.Create(new { clientId = "interop-a" }) })
        {
            noBearer.Headers.Add("Origin", fixture.RpOrigin);
            await ExpectAsync(await browser.SendAsync(noBearer), HttpStatusCode.Unauthorized);
        }
        await ExpectAsync(await browser.PostAsync(fixture.Rp("a", "logout-other"), null), HttpStatusCode.Unauthorized);
        await ExpectAsync(await browser.PostAsync(fixture.Rp("a", "logout"), null), HttpStatusCode.OK);
        await ExpectAsync(await browser.GetAsync(fixture.Rp("a", "session")), HttpStatusCode.Unauthorized);
        await ExpectAsync(await browser.PostAsync(fixture.Rp("a", "refresh-revoked"), null), HttpStatusCode.OK);
        await ExpectAsync(await otherDevice.PostAsync(fixture.Rp("a", "refresh"), null), HttpStatusCode.OK);
        await ExpectAsync(await otherDevice.GetAsync(fixture.Rp("a", "resource")), HttpStatusCode.OK);
        await ExpectAsync(await browser.GetAsync(fixture.Rp("b", "resource")), HttpStatusCode.OK);
        await LoginAsync(fixture, browser, "a", silent: true);
        var healthy = new List<double>();
        for (var round = 0; round < 3; round++)
        {
            var started = Stopwatch.GetTimestamp(); await CenterLogoutAsync(fixture, browser);
            await WaitLoggedOutAsync(fixture, browser, "a", TimeSpan.FromSeconds(15));
            await WaitLoggedOutAsync(fixture, browser, "b", TimeSpan.FromSeconds(15));
            healthy.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            await ExpectAsync(await otherDevice.GetAsync(fixture.Rp("a", "session")), HttpStatusCode.OK);
            await ExpectAsync(await otherDevice.GetAsync(fixture.Rp("a", "resource")), HttpStatusCode.OK);
            if (round < 2) { await LoginAsync(fixture, browser, "a"); await LoginAsync(fixture, browser, "b", silent: true); }
        }
        using (var response = await browser.PostAsync(fixture.Rp("a", "replay-foreign"), null))
        {
            await ExpectAsync(response, HttpStatusCode.OK, dispose: false);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); Assert.AreEqual(400, json.RootElement.GetProperty("status").GetInt32());
        }

        // 接收端真实 503 后停止并重启 Worker；不手工推进时钟或调用投递器。
        await LoginAsync(fixture, browser, "a");
        await SetFaultAsync(fixture, browser, "a", "unavailable");
        await CenterLogoutAsync(fixture, browser);
        await IdentityOidcNetworkFixture.WaitUntilAsync(async () => await fixture.ScalarAsync("SELECT COUNT(1) FROM fn_identity_oidc_logout_delivery WHERE StatusKey = 'pending' AND LastErrorCode = 'http_503'") > 0, TimeSpan.FromSeconds(15));
        await ExpectAsync(await browser.GetAsync(fixture.Rp("a", "session")), HttpStatusCode.OK);
        await fixture.StopWorkerAsync(); await SetFaultAsync(fixture, browser, "a", "ok");
        var recoveryStarted = Stopwatch.GetTimestamp(); await fixture.StartWorkerAsync();
        await WaitLoggedOutAsync(fixture, browser, "a", TimeSpan.FromSeconds(20));
        var recoveredMs = Stopwatch.GetElapsedTime(recoveryStarted).TotalMilliseconds;

        // 接收方已提交撤销但 ACK 丢失：同 jti 的再次投递必须幂等，而不是再次执行副作用。
        await LoginAsync(fixture, browser, "a"); await SetFaultAsync(fixture, browser, "a", "drop-ack-once");
        await CenterLogoutAsync(fixture, browser);
        await WaitLoggedOutAsync(fixture, browser, "a", TimeSpan.FromSeconds(15));
        await IdentityOidcNetworkFixture.WaitUntilAsync(async () =>
        {
            using var metrics = await browser.GetAsync(fixture.Rp("a", "metrics"));
            using var json = JsonDocument.Parse(await metrics.Content.ReadAsStringAsync());
            return json.RootElement.GetProperty("duplicates").GetInt32() > 0;
        }, TimeSpan.FromSeconds(20));
        await IdentityOidcNetworkFixture.WaitUntilAsync(async () => await fixture.ScalarAsync("SELECT COUNT(1) FROM fn_identity_oidc_logout_delivery WHERE StatusKey <> 'delivered'") == 0, TimeSpan.FromSeconds(10));
        await ExpectAsync(await otherDevice.GetAsync(fixture.Rp("a", "resource")), HttpStatusCode.OK);

        var probes = new List<object>();
        foreach (var concurrency in new[] { 1, 8, 32 }) probes.Add(await ProbeAsync(fixture, otherDevice, concurrency));
        IReadOnlyList<object> official = [];
        if (Environment.GetEnvironmentVariable("FULLNET_OIDC_CONFORMANCE") == "1")
        {
            await fixture.StopWorkerAsync();
            official = await IdentityOidcConformance.RunAsync(fixture);
        }
        var summary = new { provider = provider.ToString(), healthySamplesMs = healthy, recoveryAfterRestartMs = recoveredMs,
            normalDeliveryBudgetSeconds = 15, recoveryBudgetSeconds = 20, client = "openid-client 6.8.8", probes, officialModules = official,
            capacity = "Capacity-not-verified", note = "本地单 API、双 RP、单 Worker，非生产容量或外部合作方 SLO" };
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "network-result.json"), JsonSerializer.Serialize(summary));
        Console.WriteLine("OIDC_NETWORK_RESULT " + JsonSerializer.Serialize(summary));
        fixture.Passed = true;
    }

    internal static async Task LoginAsync(IdentityOidcNetworkFixture fixture, HttpClient browser, string client, bool silent = false)
    {
        using var start = await browser.GetAsync(fixture.Rp(client, "login") + (silent ? "?silent=1" : ""));
        Assert.AreEqual(HttpStatusCode.Redirect, start.StatusCode);
        using var page = await browser.GetAsync(start.Headers.Location);
        Uri? callback = page.Headers.Location;
        if (page.StatusCode == HttpStatusCode.OK)
        {
            Assert.IsFalse(silent, "prompt=none must not present a password form.");
            var fields = IdentityBrowserTestSupport.Hidden(await page.Content.ReadAsStringAsync()); fields["username"] = "admin"; fields["password"] = FullNetApiFactory.TestPassword;
            using var login = await browser.PostAsync(fixture.Op("/connect/authorize"), new FormUrlEncodedContent(fields));
            Assert.AreEqual(HttpStatusCode.Redirect, login.StatusCode); callback = login.Headers.Location;
        }
        Assert.IsNotNull(callback);
        await ExpectAsync(await browser.GetAsync(callback), HttpStatusCode.OK);
        await ExpectAsync(await browser.GetAsync(fixture.Rp(client, "session")), HttpStatusCode.OK);
    }
    internal static async Task CenterLogoutAsync(IdentityOidcNetworkFixture fixture, HttpClient browser)
    {
        var before = await fixture.ScalarAsync("SELECT COUNT(1) FROM fn_identity_oidc_center_session WHERE RevokedAtUtc IS NOT NULL");
        using var form = await browser.GetAsync(fixture.Op("/connect/logout")); Assert.AreEqual(HttpStatusCode.OK, form.StatusCode);
        var fields = IdentityBrowserTestSupport.Hidden(await form.Content.ReadAsStringAsync());
        fields["confirm"] = "yes";
        using var logout = await browser.PostAsync(fixture.Op("/connect/logout"), new FormUrlEncodedContent(fields));
        Assert.IsTrue(logout.IsSuccessStatusCode || logout.StatusCode == HttpStatusCode.Redirect);
        Assert.IsTrue(await fixture.ScalarAsync("SELECT COUNT(1) FROM fn_identity_oidc_center_session WHERE RevokedAtUtc IS NOT NULL") > before,
            "The confirmed browser center session must actually be revoked before measuring propagation.");
    }
    private static async Task SetFaultAsync(IdentityOidcNetworkFixture fixture, HttpClient browser, string client, string mode) =>
        await ExpectAsync(await browser.PostAsJsonAsync(fixture.Rp(client, "fault"), new { mode }), HttpStatusCode.OK);
    private static Task WaitLoggedOutAsync(IdentityOidcNetworkFixture fixture, HttpClient browser, string client, TimeSpan budget) =>
        IdentityOidcNetworkFixture.WaitUntilAsync(async () => { using var response = await browser.GetAsync(fixture.Rp(client, "session")); return response.StatusCode == HttpStatusCode.Unauthorized; }, budget);
    private static async Task<object> ProbeAsync(IdentityOidcNetworkFixture fixture, HttpClient browser, int concurrency)
    {
        for (var index = 0; index < 10; index++) await ExpectAsync(await browser.GetAsync(fixture.Rp("a", "resource")), HttpStatusCode.OK);
        // 默认全局 API 预算为每分钟 1200；本轮固定 600 次正常请求，不以触发限流的热循环冒充容量。
        var timings = new ConcurrentBag<double>(); var statuses = new ConcurrentDictionary<int, int>(); var errors = 0; var issued = 0; var clock = Stopwatch.StartNew();
        var process = Process.GetCurrentProcess(); var cpu = process.TotalProcessorTime; var allocation = GC.GetTotalAllocatedBytes();
        var collections = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
        await Task.WhenAll(Enumerable.Range(0, concurrency).Select(async _ =>
        {
            while (Interlocked.Increment(ref issued) <= 200)
            {
                var started = Stopwatch.GetTimestamp();
                using var response = await browser.GetAsync(fixture.Rp("a", "resource"));
                statuses.AddOrUpdate((int)response.StatusCode, 1, (_, count) => count + 1);
                if (response.StatusCode != HttpStatusCode.OK) Interlocked.Increment(ref errors);
                timings.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
        }));
        var elapsed = clock.Elapsed.TotalSeconds; var sorted = timings.Order().ToArray(); Assert.AreEqual(200, sorted.Length);
        Console.WriteLine("OIDC_LOAD_STATUS " + JsonSerializer.Serialize(new { concurrency, statuses })); Assert.AreEqual(0, errors);
        process.Refresh();
        double Percentile(double percentile) => sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * percentile) - 1)];
        return new { concurrency, requests = sorted.Length, elapsedSeconds = elapsed, throughput = sorted.Length / elapsed, errors, statuses,
            p50Ms = Percentile(.50), p95Ms = Percentile(.95), p99Ms = Percentile(.99), apiAndDriverCpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds,
            apiAndDriverWorkingSetBytes = process.WorkingSet64, apiAndDriverAllocatedBytes = GC.GetTotalAllocatedBytes() - allocation,
            gc = Enumerable.Range(0, 3).Select(index => GC.CollectionCount(index) - collections[index]).ToArray(),
            databaseCpuIoPoolWait = "not-collected" };
    }
    private static async Task ExpectAsync(HttpResponseMessage response, HttpStatusCode expected, bool dispose = true)
    {
        try { Assert.AreEqual(expected, response.StatusCode, "Network RP: " + await response.Content.ReadAsStringAsync()); }
        finally { if (dispose) response.Dispose(); }
    }
}
