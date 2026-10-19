using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Messaging;
using Full.NET.Modules.Auditing.Features.QueryLogTimeline;
using Full.NET.Modules.ObservabilityAdmin.Features.QueryLogTimeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.ObservabilityAdmin;

/// <summary>真实 HTTPS ES 通过正式 API 验证受限读取、关联交集与安全摘要，不替代 SQL 双库验收。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ElasticsearchTimelineIntegrationTests
{
    [TestMethod]
    public async Task Https_read_only_key_correlates_exact_metadata_and_reports_partial_or_unavailable()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        var token = deadline.Token;
        await using var es = await KafkaRequestElasticsearchFixture.StartTlsAsync(token);
        var index = "fn-logs-timeline-" + Guid.NewGuid().ToString("N");
        using (var created = await es.SendAdminAsync(HttpMethod.Put, index,
                   """{"mappings":{"properties":{"@t":{"type":"date"}}}}""", token))
            Assert.AreEqual(HttpStatusCode.OK, created.StatusCode);

        var keyBody = JsonSerializer.Serialize(new
        {
            name = "timeline-reader-" + Guid.NewGuid().ToString("N"),
            role_descriptors = new
            {
                reader = new
                {
                    cluster = Array.Empty<string>(),
                    indices = new[] { new { names = new[] { index }, privileges = new[] { "read" } } },
                },
            },
        });
        using var keyResponse = await es.SendAdminAsync(HttpMethod.Post, "_security/api_key", keyBody, token);
        Assert.AreEqual(HttpStatusCode.OK, keyResponse.StatusCode);
        using var keyJson = JsonDocument.Parse(await keyResponse.Content.ReadAsStringAsync(token));
        var key = keyJson.RootElement.GetProperty("encoded").GetString()!;
        var trace = new string('a', 32);
        var session = Guid.NewGuid(); var user = Guid.NewGuid();
        var ip = new string('c', 64);
        var start = DateTimeOffset.UtcNow.AddMinutes(-2);
        var ids = Enumerable.Range(0, 8).Select(_ => Guid.CreateVersion7().ToString("D")).ToArray();
        for (var i = 0; i < ids.Length; i++)
        {
            var data = new Dictionary<string, object?>
            {
                ["@t"] = i == 7 ? start.AddHours(-1) : start.AddSeconds(i),
                ["LogEventId"] = ids[i], ["TraceId"] = i == 3 ? new string('b', 32) : trace,
                ["UserId"] = i == 5 ? Guid.NewGuid() : user,
                [i == 1 ? "ApplicationSessionId" : i == 2 ? "CenterSessionId" : "SessionId"] = i == 4 ? Guid.NewGuid() : session,
                ["ClientIpFingerprint"] = i == 6 ? new string('d', 64) : ip,
                ["@mt"] = "private-timeline-body-must-not-escape",
            };
            using var written = await es.SendAdminAsync(HttpMethod.Put, index + "/_doc/" + ids[i] + "?refresh=true",
                JsonSerializer.Serialize(data), token);
            Assert.AreEqual(HttpStatusCode.Created, written.StatusCode);
        }

        // 只读 Key 不能写文档，也不能读取授权范围外的索引。
        using (var restricted = es.CreateTrustedClient())
        {
            restricted.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", key);
            using var deniedWrite = await restricted.PutAsync(new Uri(es.Url, index + "/_doc/denied"),
                new StringContent("{}", Encoding.UTF8, "application/json"), token);
            Assert.AreEqual(HttpStatusCode.Forbidden, deniedWrite.StatusCode);
            using var deniedIndex = await restricted.GetAsync(new Uri(es.Url, "fn-logs-outside-reader/_search"), token);
            Assert.AreEqual(HttpStatusCode.Forbidden, deniedIndex.StatusCode);
        }

        using var factory = new FullNetApiFactory(DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync(), new Dictionary<string, string?>
            {
                [RuntimeLogTimelineOptions.SectionName + ":ElasticsearchEnabled"] = "true",
                [RuntimeLogTimelineOptions.SectionName + ":ElasticsearchEndpoint"] = es.Url.ToString(),
                [RuntimeLogTimelineOptions.SectionName + ":ElasticsearchIndexPattern"] = index,
                [RuntimeLogTimelineOptions.SectionName + ":ElasticsearchApiKey"] = key,
            }, configureTestServices: services => services.AddHttpClient("ElasticsearchTimelineReader")
                .ConfigurePrimaryHttpMessageHandler(() => new TrustedCaHandler(es.CaPath!)));
        await factory.InitializeAsync(token);
        using var client = factory.CreateClientForHost("localhost");
        var url = "/api/v1/auditing/timeline?traceId=" + trace
            + "&fromUtc=" + Uri.EscapeDataString(start.AddSeconds(-1).ToString("O"))
            + "&toUtc=" + Uri.EscapeDataString(start.AddMinutes(1).ToString("O"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await factory.CreateHostAccessTokenAsync([LogTimelinePermissions.Read], token));
        var denied = await client.GetFromJsonAsync<LogTimelineResponse>(url, token);
        Assert.IsNotNull(denied);
        Assert.AreEqual("forbidden", denied.Sources.Single(source => source.Source == "runtime.elasticsearch").Status);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await factory.CreateHostAccessTokenAsync([LogTimelinePermissions.Read, "observability.log_search.read"], token));
        var filtered = url + "&sessionId=" + session + "&userId=" + user + "&clientIpFingerprint=" + ip;
        using var raw = await client.GetAsync(filtered, token);
        Assert.AreEqual(HttpStatusCode.OK, raw.StatusCode);
        var body = await raw.Content.ReadAsStringAsync(token);
        Assert.IsFalse(body.Contains("private-timeline-body", StringComparison.Ordinal));
        var response = await raw.Content.ReadFromJsonAsync<LogTimelineResponse>(token);
        Assert.IsNotNull(response);
        CollectionAssert.AreEqual(ids.Take(3).ToArray(), response.Entries.Select(entry => entry.Id).ToArray());
        Assert.AreEqual("available", response.Sources.Single(source => source.Source == "runtime.elasticsearch").Status);
        Assert.AreEqual(session, response.Entries[1].Correlation.ApplicationSessionId);
        Assert.AreEqual(session, response.Entries[2].Correlation.CenterSessionId);
        Assert.IsTrue(response.Entries.All(entry => entry.Correlation.UserId == user
            && entry.Correlation.TraceId == trace && entry.Correlation.ClientIpFingerprint == ip));
        var truncated = await client.GetFromJsonAsync<LogTimelineResponse>(filtered + "&limit=1", token);
        Assert.IsNotNull(truncated);
        Assert.HasCount(1, truncated.Entries);
        Assert.AreEqual("truncated", truncated.Sources.Single(source => source.Source == "runtime.elasticsearch").Status);
        Assert.IsTrue(truncated.Partial);
        var traceOnly = await client.GetFromJsonAsync<LogTimelineResponse>(url, token);
        Assert.IsNotNull(traceOnly);
        Assert.HasCount(6, traceOnly.Entries);

        factory.Services.GetRequiredService<IOptions<RuntimeLogTimelineOptions>>().Value.ElasticsearchApiKey = "invalid";
        var unavailable = await client.GetFromJsonAsync<LogTimelineResponse>(filtered, token);
        Assert.IsNotNull(unavailable);
        Assert.AreEqual("unavailable", unavailable.Sources.Single(source => source.Source == "runtime.elasticsearch").Status);
        Assert.HasCount(0, unavailable.Entries);
    }

    /// <summary>测试私有 CA 只注入具名客户端；生产传输与系统信任配置保持正式注册。</summary>
    private sealed class TrustedCaHandler : HttpClientHandler
    {
        private readonly System.Security.Cryptography.X509Certificates.X509Certificate2 _root;
        public TrustedCaHandler(string caPath)
        {
            AllowAutoRedirect = false;
            UseProxy = false;
            _root = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificateFromFile(caPath);
            ServerCertificateCustomValidationCallback = (_, certificate, _, errors) =>
            {
                if (certificate is null || (errors & System.Net.Security.SslPolicyErrors.RemoteCertificateNameMismatch) != 0)
                    return false;
                using var chain = new System.Security.Cryptography.X509Certificates.X509Chain();
                chain.ChainPolicy.TrustMode = System.Security.Cryptography.X509Certificates.X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.RevocationMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck;
                chain.ChainPolicy.CustomTrustStore.Add(_root);
                return chain.Build(certificate);
            };
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) _root.Dispose();
        }
    }
}
