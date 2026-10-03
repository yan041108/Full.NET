using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Full.NET.LogConsumer;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>真实请求闭环使用的本地 ES，可提供明文夹具或私有 CA HTTPS 与受限 API Key。</summary>
internal sealed class KafkaRequestElasticsearchFixture : IAsyncDisposable
{
    private readonly IContainer _container;
    private readonly Uri _uri;
    private readonly HttpClient _plain;
    private readonly HttpClient _bulk;

    private KafkaRequestElasticsearchFixture(IContainer container, Uri uri,
        HttpClient? read = null, HttpClient? bulk = null, string? caPath = null, string? apiKey = null)
    {
        _container = container;
        _uri = uri;
        _plain = read ?? new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false })
            { Timeout = TimeSpan.FromSeconds(10) };
        _bulk = bulk ?? new HttpClient(new TestBulkTransport(uri)
        {
            InnerHandler = new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false },
        }) { Timeout = TimeSpan.FromSeconds(10) };
        CaPath = caPath;
        ApiKey = apiKey;
        Sink = new ElasticsearchLogDocumentSink(_bulk, caPath is null ? new Uri("https://es-request-test.invalid/") : uri);
    }

    public ElasticsearchLogDocumentSink Sink { get; }
    public Uri Url => _uri;
    public string? CaPath { get; }
    public string? ApiKey { get; }

    public static async Task<KafkaRequestElasticsearchFixture> StartTlsAsync(CancellationToken token)
    {
        const string password = "fullnet-request-es-test-only";
        using var caKey = RSA.Create(2048);
        var caRequest = new CertificateRequest("CN=FullNET Request ES CA", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        caRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        var from = DateTimeOffset.UtcNow.AddMinutes(-5);
        var until = from.AddHours(1);
        using var ca = caRequest.CreateSelfSigned(from, until);
        using var serverKey = RSA.Create(2048);
        var serverRequest = new CertificateRequest("CN=localhost", serverKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddDnsName("host.docker.internal");
        san.AddIpAddress(IPAddress.Loopback);
        // 与 Testcontainers 的实际宿主覆盖保持一致，不能依靠跳过主机名校验。
        var brokerHost = Environment.GetEnvironmentVariable("TESTCONTAINERS_HOST_OVERRIDE")?.Trim();
        if (!string.IsNullOrWhiteSpace(brokerHost))
        {
            if (IPAddress.TryParse(brokerHost, out var address)) san.AddIpAddress(address);
            else if (brokerHost is not ("localhost" or "host.docker.internal")) san.AddDnsName(brokerHost);
        }
        serverRequest.CertificateExtensions.Add(san.Build());
        serverRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        serverRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], true));
        using var server = serverRequest.Create(ca, from.AddMinutes(1), until.AddMinutes(-1), RandomNumberGenerator.GetBytes(16));
        var caPath = Path.Combine(Path.GetTempPath(), $"fullnet-request-es-{Guid.NewGuid():N}.crt");
        await File.WriteAllTextAsync(caPath, ca.ExportCertificatePem(), token);
        var container = new ContainerBuilder(
                "docker.elastic.co/elasticsearch/elasticsearch@sha256:82ac14f43fe701992e601f4cc81e1c0d7dbc5a2576d8cd736006452925df4026")
            .WithResourceMapping(Encoding.UTF8.GetBytes(server.ExportCertificatePem()), "/usr/share/elasticsearch/config/certs/http.crt", 1000, 0)
            .WithResourceMapping(Encoding.UTF8.GetBytes(serverKey.ExportPkcs8PrivateKeyPem()), "/usr/share/elasticsearch/config/certs/http.key", 1000, 0)
            .WithEnvironment("discovery.type", "single-node")
            .WithEnvironment("xpack.security.enabled", "true")
            .WithEnvironment("xpack.security.autoconfiguration.enabled", "false")
            .WithEnvironment("xpack.security.http.ssl.enabled", "true")
            .WithEnvironment("xpack.security.http.ssl.key", "certs/http.key")
            .WithEnvironment("xpack.security.http.ssl.certificate", "certs/http.crt")
            .WithEnvironment("xpack.ml.enabled", "false")
            .WithEnvironment("ELASTIC_PASSWORD", password)
            .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
            .WithPortBinding(9200, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(9200))
            .Build();
        HttpClient? read = null;
        HttpClient? bulk = null;
        try
        {
            await container.StartAsync(token);
            var uri = new UriBuilder("https", container.Hostname, container.GetMappedPublicPort(9200)).Uri;
            HttpClient NewTrustedClient()
            {
                // 临时 CA 无 CRL，仅测试 Development 使用 NoCheck，仍校验信任链与主机名。
                var policy = new X509ChainPolicy { TrustMode = X509ChainTrustMode.CustomRootTrust, RevocationMode = X509RevocationMode.NoCheck };
                policy.CustomTrustStore.Add(X509CertificateLoader.LoadCertificateFromFile(caPath));
                var handler = new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false };
                handler.SslOptions.CertificateChainPolicy = policy;
                return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            }
            read = NewTrustedClient();
            read.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("elastic:" + password)));
            using var readiness = CancellationTokenSource.CreateLinkedTokenSource(token);
            readiness.CancelAfter(TimeSpan.FromSeconds(90));
            while (true)
            {
                readiness.Token.ThrowIfCancellationRequested();
                try
                {
                    using var health = await read.GetAsync(uri, readiness.Token);
                    if (health.StatusCode == HttpStatusCode.OK) break;
                }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) when (!readiness.IsCancellationRequested) { }
                await Task.Delay(250, readiness.Token);
            }
            var keyRequest = JsonSerializer.Serialize(new
            {
                name = "fullnet-request-" + Guid.NewGuid().ToString("N"),
                role_descriptors = new
                {
                    writer = new
                    {
                        cluster = Array.Empty<string>(),
                        indices = new[] { new { names = new[] { "fn-logs-1-*" }, privileges = new[] { "write", "auto_configure" } } },
                    },
                },
            });
            using var keyResponse = await read.PostAsync(new Uri(uri, "_security/api_key"), new StringContent(keyRequest, Encoding.UTF8, "application/json"), token);
            Assert.AreEqual(HttpStatusCode.OK, keyResponse.StatusCode);
            using var keyJson = JsonDocument.Parse(await keyResponse.Content.ReadAsStringAsync(token));
            var apiKey = keyJson.RootElement.GetProperty("encoded").GetString()!;
            Assert.IsFalse(string.IsNullOrWhiteSpace(apiKey));
            bulk = NewTrustedClient();
            bulk.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", apiKey);
            return new(container, uri, read, bulk, caPath, apiKey);
        }
        catch
        {
            read?.Dispose();
            bulk?.Dispose();
            try { await container.DisposeAsync(); }
            finally { File.Delete(caPath); }
            throw;
        }
    }

    public static async Task<KafkaRequestElasticsearchFixture> StartAsync(CancellationToken token)
    {
        var container = new ContainerBuilder(
                "docker.elastic.co/elasticsearch/elasticsearch@sha256:82ac14f43fe701992e601f4cc81e1c0d7dbc5a2576d8cd736006452925df4026")
            .WithEnvironment("discovery.type", "single-node")
            .WithEnvironment("xpack.security.enabled", "false")
            .WithEnvironment("xpack.ml.enabled", "false")
            .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
            .WithPortBinding(9200, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(9200).ForPath("/")))
            .Build();
        try
        {
            await container.StartAsync(token);
            return new(container, new UriBuilder("http", container.Hostname, container.GetMappedPublicPort(9200)).Uri);
        }
        catch
        {
            await container.DisposeAsync();
            throw;
        }
    }

    public async Task<object> VerifyAsync(IReadOnlyList<string> lines, CancellationToken token)
    {
        var byIndex = new Dictionary<string, Dictionary<string, JsonNode>>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            using var json = JsonDocument.Parse(line);
            var id = json.RootElement.GetProperty("LogEventId").GetString()!;
            Assert.AreEqual(LogRecordValidationResult.Valid,
                KafkaLogRecordParser.TryParse(id, Encoding.UTF8.GetBytes(line), 16384, out var record));
            Assert.IsTrue(record!.TryGetIndexName(new Dictionary<int, int> { [1] = 30 }, DateTimeOffset.UtcNow, out var index));
            var occurred = json.RootElement.GetProperty("OccurredAtUtc").GetDateTimeOffset();
            var classification = json.RootElement.GetProperty("log.class").GetString();
            Assert.AreEqual($"fn-logs-1-{classification}-{occurred.UtcDateTime:yyyy.MM.dd}", index,
                "目标名称须独立符合冻结版本、分类与事件 UTC 日期。");
            if (!byIndex.TryGetValue(index!, out var documents)) byIndex[index!] = documents = new(StringComparer.Ordinal);
            Assert.IsTrue(documents.TryAdd(id, JsonNode.Parse(line)!));
        }
        var verified = 0;
        foreach (var (index, documents) in byIndex)
        {
            using var refresh = await _plain.PostAsync(new Uri(_uri, index + "/_refresh"), null, token);
            Assert.AreEqual(HttpStatusCode.OK, refresh.StatusCode);
            using var countResponse = await _plain.GetAsync(new Uri(_uri, index + "/_count"), token);
            Assert.AreEqual(HttpStatusCode.OK, countResponse.StatusCode);
            using var count = JsonDocument.Parse(await countResponse.Content.ReadAsStringAsync(token));
            Assert.AreEqual((long)documents.Count, count.RootElement.GetProperty("count").GetInt64());
            foreach (var chunk in documents.Keys.Chunk(200))
            {
                using var content = new StringContent(JsonSerializer.Serialize(new { ids = chunk }), Encoding.UTF8, "application/json");
                using var response = await _plain.PostAsync(new Uri(_uri, index + "/_mget"), content, token);
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var document in body.RootElement.GetProperty("docs").EnumerateArray())
                {
                    var id = document.GetProperty("_id").GetString()!;
                    Assert.IsTrue(chunk.Contains(id) && seen.Add(id) && document.GetProperty("found").GetBoolean());
                    Assert.AreEqual(index, document.GetProperty("_index").GetString());
                    Assert.IsTrue(JsonNode.DeepEquals(documents[id], JsonNode.Parse(document.GetProperty("_source").GetRawText())),
                        "ES 来源字段与 Broker 快照必须逐项一致。");
                    verified++;
                }
                Assert.AreEqual(chunk.Length, seen.Count);
            }
        }
        Assert.AreEqual(lines.Count, verified);
        return new { verifiedDocuments = verified, indexCount = byIndex.Count, finalOffsetsConfirmed = true, isolationCount = 0 };
    }

    public async ValueTask DisposeAsync()
    {
        _plain.Dispose();
        _bulk.Dispose();
        try { await _container.DisposeAsync(); }
        finally { if (CaPath is not null) File.Delete(CaPath); }
    }

    private sealed class TestBulkTransport(Uri target) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.AreEqual("https://es-request-test.invalid/_bulk", request.RequestUri!.ToString());
            request.RequestUri = new Uri(target, "_bulk");
            return base.SendAsync(request, token);
        }
    }
}
