using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>运行锁定摘要的 OIDF 官方套件；只选择已开放的 Discovery 与强制 S256 授权码路径。</summary>
internal static class IdentityOidcConformance
{
    internal const string Image = "registry.gitlab.com/openid/conformance-suite@sha256:317614a73c69573907977c0a80a711e66742e9a4e3778c7f35f26cf50adcaef2";
    internal static async Task<IReadOnlyList<object>> RunAsync(IdentityOidcNetworkFixture fixture)
    {
        var suffix = Guid.NewGuid().ToString("N"); var mongo = "fullnet-oidf-mongo-" + suffix; var server = "fullnet-oidf-suite-" + suffix;
        var started = new List<string>(); var results = new List<object>();
        try
        {
            await IdentityOidcNetworkFixture.RunDockerAsync("run", "-d", "--rm", "--name", mongo, "--network", fixture.Network, "--network-alias", "mongodb", "--memory", "1g", "--cpus", "1",
                "--tmpfs", "/data/db:rw,nosuid,nodev,size=536870912", "mongo:6.0.13", "--wiredTigerCacheSizeGB", "0.25"); started.Add(mongo);
            // 信任仅加入此临时 JVM 的公钥信任库，不修改机器根证书，也不关闭 TLS 验证。
            var command = "keytool -importcert -noprompt -alias fullnet-fixture -file /run/oidc/tls.pem -keystore /tmp/fullnet-truststore -storepass fixture-only >/dev/null 2>&1 && exec java -Xmx2g "
                + "-Djavax.net.ssl.trustStore=/tmp/fullnet-truststore -Djavax.net.ssl.trustStorePassword=fixture-only "
                + "-Dfintechlabs.devmode=true -Dfintechlabs.startredir=true -Dserver.forward-headers-strategy=framework "
                + "-Dspring.mongodb.uri=mongodb://mongodb:27017/test_suite -Dfintechlabs.base_url=" + fixture.SuiteOrigin + " -jar /server/fapi-test-suite.jar";
            await IdentityOidcNetworkFixture.RunDockerAsync("run", "-d", "--rm", "--name", server, "--network", fixture.Network, "--memory", "3g", "--cpus", "2",
                "-p", $"127.0.0.1:{fixture.SuiteHttpPort}:8080", "--mount", $"type=bind,source={Path.Combine(fixture.Root, "tls.pem")},target=/run/oidc/tls.pem,readonly", "--entrypoint", "sh", Image, "-c", command); started.Add(server);
            using var api = fixture.Browser();
            await IdentityOidcNetworkFixture.WaitUntilAsync(async () =>
            {
                try { using var response = await api.GetAsync(fixture.SuiteOrigin + "/api/runner/available"); return response.IsSuccessStatusCode; }
                catch (HttpRequestException) { return false; }
                catch (TaskCanceledException) { return false; }
            }, TimeSpan.FromSeconds(90));
            foreach (var (module, authentication, client) in new[] {
                ("oidcc-discovery-endpoint-verification", "", "suite-primary"),
                ("oidcc-ensure-request-with-valid-pkce-succeeds", "client_secret_basic", "suite-primary"),
                ("oidcc-ensure-request-with-valid-pkce-succeeds", "client_secret_post", "suite-post") })
            {
                var variant = new Dictionary<string, string> { ["server_metadata"] = "discovery", ["client_registration"] = "static_client" };
                if (authentication.Length > 0) { variant["response_type"] = "code"; variant["response_mode"] = "default"; variant["client_auth_type"] = authentication; }
                var configuration = new { alias = "fullnet-interop", description = "Full.NET local supported-profile verification",
                    server = new { discoveryUrl = fixture.Issuer.TrimEnd('/') + "/.well-known/openid-configuration" },
                    client = new { client_id = client, client_secret = fixture.Secrets[client], scope = "openid profile" },
                    client2 = new { client_id = "suite-secondary", client_secret = fixture.Secrets["suite-secondary"], scope = "openid profile" } };
                using var create = await api.PostAsJsonAsync(fixture.SuiteOrigin + "/api/runner?test=" + module + "&variant=" + Uri.EscapeDataString(JsonSerializer.Serialize(variant)), configuration);
                Assert.AreEqual(HttpStatusCode.Created, create.StatusCode, "Official suite module creation failed: " + module);
                using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync()); var id = created.RootElement.GetProperty("id").GetString()!;
                using var begin = await api.PostAsync(fixture.SuiteOrigin + "/api/runner/" + id, new StringContent("{}", Encoding.UTF8, "application/json"));
                Assert.IsTrue(begin.IsSuccessStatusCode, "Official suite module did not start: " + module);
                using var browser = fixture.Browser(); var visited = new HashSet<string>(StringComparer.Ordinal); var clock = Stopwatch.StartNew();
                string status = "", result = "";
                await IdentityOidcNetworkFixture.WaitUntilAsync(async () =>
                {
                    using var info = await api.GetAsync(fixture.SuiteOrigin + "/api/info/" + id);
                    using var state = JsonDocument.Parse(await info.Content.ReadAsStringAsync());
                    status = state.RootElement.TryGetProperty("status", out var value) ? value.GetString() ?? "" : "";
                    result = state.RootElement.TryGetProperty("result", out value) ? value.GetString() ?? "" : "";
                    if (status is "FINISHED" or "INTERRUPTED") return true;
                    using var request = await api.GetAsync(fixture.SuiteOrigin + "/api/runner/browser/" + id);
                    if (!request.IsSuccessStatusCode) return false;
                    using var urls = JsonDocument.Parse(await request.Content.ReadAsStringAsync());
                    if (urls.RootElement.TryGetProperty("urls", out var list))
                    {
                        foreach (var link in list.EnumerateArray())
                        {
                            var url = link.GetString()!; if (!visited.Add(url)) continue;
                            await NavigateAsync(fixture, browser, new Uri(url));
                            using var marked = await api.PostAsync(fixture.SuiteOrigin + "/api/runner/browser/" + id + "/visit?url=" + Uri.EscapeDataString(url), null);
                            Assert.IsTrue(marked.IsSuccessStatusCode);
                        }
                    }
                    return false;
                }, TimeSpan.FromSeconds(90));
                var outcome = new { module, authentication, status, result, elapsedSeconds = clock.Elapsed.TotalSeconds };
                results.Add(outcome); Console.WriteLine("OIDF_MODULE_RESULT " + JsonSerializer.Serialize(outcome));
                await File.WriteAllTextAsync(Path.Combine(fixture.Root, "official-conformance-result.json"), JsonSerializer.Serialize(new { image = Image, results }));
                if (result != "PASSED")
                {
                    using var response = await api.GetAsync(fixture.SuiteOrigin + "/api/log/" + id);
                    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    var failures = document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.EnumerateArray()
                        .Where(entry => entry.TryGetProperty("result", out var severity) && severity.GetString() is "FAILURE" or "WARNING")
                        .Select(entry => new { source = entry.TryGetProperty("src", out var source) ? source.GetString() : null,
                            message = entry.TryGetProperty("msg", out var message) ? message.GetString() : null }).ToArray() : [];
                    await File.WriteAllTextAsync(Path.Combine(fixture.Root, "official-conformance-failures.json"), JsonSerializer.Serialize(failures));
                    Assert.Fail("Official OIDF module did not pass: " + JsonSerializer.Serialize(outcome) + "; conditions: " + JsonSerializer.Serialize(failures));
                }
            }
        }
        finally
        {
            var cleanupErrors = new List<string>();
            foreach (var name in started.AsEnumerable().Reverse())
            {
                try { await IdentityOidcNetworkFixture.RunDockerAsync("stop", "--time", "5", name); }
                catch (Exception error) { cleanupErrors.Add(error.GetType().Name); }
            }
            Assert.IsEmpty(cleanupErrors, "Owned official suite cleanup failed: " + string.Join(", ", cleanupErrors));
        }
        return results;
    }
    private static async Task NavigateAsync(IdentityOidcNetworkFixture fixture, HttpClient browser, Uri target)
    {
        for (var step = 0; step < 12; step++)
        {
            using var response = await browser.GetAsync(target);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is Uri location) { target = new Uri(target, location); continue; }
            var content = await response.Content.ReadAsStringAsync();
            if (target.GetLeftPart(UriPartial.Authority) == fixture.SuiteOrigin && target.AbsolutePath == "/test/a/fullnet-interop/callback")
            {
                // 官方回调页面即使是 code flow 也需执行一次空 fragment 回传，随后套件才继续兑换和校验。
                var script = Regex.Match(content, "xhr\\.open\\('POST',\\s*(\"(?:\\\\.|[^\"\\\\])*\")", RegexOptions.CultureInvariant);
                Assert.IsTrue(script.Success, "Official callback fragment submission script was not found.");
                var submission = new Uri(JsonSerializer.Deserialize<string>(script.Groups[1].Value)!);
                Assert.IsTrue(submission.GetLeftPart(UriPartial.Authority) == fixture.SuiteOrigin
                    && submission.AbsolutePath.StartsWith("/test/a/fullnet-interop/", StringComparison.Ordinal), "Unexpected official callback submission target.");
                using var submitted = await browser.PostAsync(submission, new StringContent(target.Fragment, Encoding.UTF8, "text/plain"));
                Assert.IsTrue(submitted.IsSuccessStatusCode, "Official callback fragment submission failed.");
                return;
            }
            if (target.AbsolutePath == "/connect/authorize" && content.Contains("name=\"username\"", StringComparison.Ordinal))
            {
                var fields = IdentityBrowserTestSupport.Hidden(content); fields["username"] = "admin"; fields["password"] = FullNetApiFactory.TestPassword;
                using var login = await browser.PostAsync(fixture.Op("/connect/authorize"), new FormUrlEncodedContent(fields));
                Assert.AreEqual(HttpStatusCode.Redirect, login.StatusCode, "Official suite authorization could not complete.");
                target = new Uri(target, login.Headers.Location!); continue;
            }
            Assert.IsTrue(response.IsSuccessStatusCode, "Official suite browser navigation failed at " + target.AbsolutePath + ": " + (int)response.StatusCode);
            return;
        }
        Assert.Fail("Official suite redirect limit exceeded.");
    }
}
