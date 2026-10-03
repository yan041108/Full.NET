using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using DotNet.Testcontainers.Builders;
using Full.NET.LogConsumer;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>固定版 Elasticsearch 上验证消费者所需的私有 CA 与 API Key 边界。</summary>
[TestClass]
[DoNotParallelize]
public sealed class KafkaLogConsumerElasticsearchTlsTests
{
    private const string TestPassword = "fullnet-es-tls-test-only";

    [TestMethod]
    public async Task PrivateCaAndApiKeyAreRequiredForBulkConfirmation()
    {
        using var caKey = RSA.Create(2048);
        var caRequest = new CertificateRequest(
            "CN=FullNET ES Test CA", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        caRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        var validFrom = DateTimeOffset.UtcNow.AddMinutes(-5);
        var validUntil = validFrom.AddHours(1);
        using var ca = caRequest.CreateSelfSigned(validFrom, validUntil);
        using var serverKey = RSA.Create(2048);
        var serverRequest = new CertificateRequest(
            "CN=localhost", serverKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        // Docker Desktop 的容器化测试经宿主网关访问映射端口，仍执行真实主机名校验。
        san.AddDnsName("host.docker.internal");
        san.AddIpAddress(IPAddress.Loopback);
        serverRequest.CertificateExtensions.Add(san.Build());
        serverRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        serverRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid("1.3.6.1.5.5.7.3.1")], true));
        using var server = serverRequest.Create(ca, validFrom.AddMinutes(1),
            validUntil.AddMinutes(-1), RandomNumberGenerator.GetBytes(16));

        var caPath = Path.Combine(Path.GetTempPath(), $"fullnet-es-test-ca-{Guid.NewGuid():N}.crt");
        await File.WriteAllTextAsync(caPath, ca.ExportCertificatePem());
        try
        {
            await using var elasticsearch = new ContainerBuilder(
                "docker.elastic.co/elasticsearch/elasticsearch@sha256:82ac14f43fe701992e601f4cc81e1c0d7dbc5a2576d8cd736006452925df4026")
                .WithResourceMapping(Encoding.UTF8.GetBytes(server.ExportCertificatePem()),
                    "/usr/share/elasticsearch/config/certs/http.crt", 1000, 0)
                .WithResourceMapping(Encoding.UTF8.GetBytes(serverKey.ExportPkcs8PrivateKeyPem()),
                    "/usr/share/elasticsearch/config/certs/http.key", 1000, 0)
                .WithEnvironment("discovery.type", "single-node")
                .WithEnvironment("xpack.security.enabled", "true")
                .WithEnvironment("xpack.security.autoconfiguration.enabled", "false")
                .WithEnvironment("xpack.security.http.ssl.enabled", "true")
                .WithEnvironment("xpack.security.http.ssl.key", "certs/http.key")
                .WithEnvironment("xpack.security.http.ssl.certificate", "certs/http.crt")
                .WithEnvironment("xpack.ml.enabled", "false")
                .WithEnvironment("ELASTIC_PASSWORD", TestPassword)
                .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
                .WithPortBinding(9200, assignRandomHostPort: true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(9200))
                .Build();
            await elasticsearch.StartAsync();
            var esUri = new UriBuilder("https", elasticsearch.Hostname, elasticsearch.GetMappedPublicPort(9200)).Uri;
            // 临时测试 CA 没有 CRL；仅此测试进程显式关闭吊销检查，生产仍要求在线检查。
            var policy = new X509ChainPolicy
            {
                TrustMode = X509ChainTrustMode.CustomRootTrust,
                RevocationMode = X509RevocationMode.NoCheck,
            };
            policy.CustomTrustStore.Add(X509CertificateLoader.LoadCertificateFromFile(caPath));
            using var trustedHandler = new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false };
            trustedHandler.SslOptions.CertificateChainPolicy = policy;
            using var http = new HttpClient(trustedHandler) { Timeout = TimeSpan.FromSeconds(5) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("elastic:" + TestPassword)));
            var ready = false;
            var deadline = DateTimeOffset.UtcNow.AddMinutes(1);
            while (DateTimeOffset.UtcNow < deadline)
            {
                try
                {
                    using var health = await http.GetAsync(esUri);
                    ready = health.StatusCode == HttpStatusCode.OK;
                    if (ready) break;
                }
                catch (HttpRequestException)
                {
                    // 端口开放早于 HTTPS 与安全索引就绪，等待真实授权响应。
                }
                catch (TaskCanceledException)
                {
                    // 启动阶段的单次请求超时不代表证书或认证失败。
                }
                await Task.Delay(500);
            }
            Assert.IsTrue(ready, "受信客户端未能在期限内完成 ES HTTPS 健康请求。");
            using var untrusted = new HttpClient(new SocketsHttpHandler { UseProxy = false });
            var trustFailure = await Assert.ThrowsAsync<HttpRequestException>(
                () => untrusted.GetAsync(esUri));
            Assert.IsInstanceOfType<AuthenticationException>(trustFailure.InnerException);

            var id = Guid.CreateVersion7().ToString("D");
            var occurred = DateTimeOffset.UtcNow.AddMinutes(-1);
            var expiry = occurred.AddDays(30);
            var indexName = $"fn-logs-2-diagnostic-{occurred:yyyy.MM.dd}";
            using (var created = await http.PutAsync(new Uri(esUri, indexName),
                       new StringContent("{}", Encoding.UTF8, "application/json")))
            {
                Assert.AreEqual(HttpStatusCode.OK, created.StatusCode);
            }
            var keyRequest = JsonSerializer.Serialize(new
            {
                name = "fullnet-es-test-" + Guid.NewGuid().ToString("N"),
                role_descriptors = new
                {
                    writer = new
                    {
                        cluster = Array.Empty<string>(),
                        indices = new[] { new { names = new[] { indexName }, privileges = new[] { "write" } } },
                    },
                },
            });
            using var keyResponse = await http.PostAsync(new Uri(esUri, "_security/api_key"),
                new StringContent(keyRequest, Encoding.UTF8, "application/json"));
            Assert.AreEqual(HttpStatusCode.OK, keyResponse.StatusCode);
            using var keyJson = JsonDocument.Parse(await keyResponse.Content.ReadAsStringAsync());
            var encodedKey = keyJson.RootElement.GetProperty("encoded").GetString();
            Assert.IsFalse(string.IsNullOrWhiteSpace(encodedKey));

            var json = $$"""
                {"@t":"{{occurred:O}}","@mt":"tls","LogEventId":"{{id}}","log.class":"diagnostic","OccurredAtUtc":"{{occurred:O}}","ExpiresAtUtc":"{{expiry:O}}","IndexRouteVersion":2}
                """;
            Assert.AreEqual(LogRecordValidationResult.Valid,
                KafkaLogRecordParser.TryParse(id, Encoding.UTF8.GetBytes(json), 1024, out var record));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", "invalid");
            var sink = new ElasticsearchLogDocumentSink(http, esUri);
            Assert.AreEqual(BulkItemOutcome.Retry,
                await sink.WriteAsync(record!, indexName, default));

            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", encodedKey);
            Assert.AreEqual(BulkItemOutcome.Succeeded,
                await sink.WriteAsync(record!, indexName, default));
            // 同一 HTTPS/最小权限 API Key 输出批次，重放保持固定 ID，不能生成额外文档。
            var batchWrites = new List<LogDocumentWrite>();
            foreach (var batchId in new[] { Guid.CreateVersion7().ToString("D"), Guid.CreateVersion7().ToString("D") })
            {
                var batchJson = json.Replace(id, batchId, StringComparison.Ordinal);
                Assert.AreEqual(LogRecordValidationResult.Valid,
                    KafkaLogRecordParser.TryParse(batchId, Encoding.UTF8.GetBytes(batchJson), 1024, out var batchRecord));
                batchWrites.Add(new LogDocumentWrite(batchRecord!, indexName));
            }
            for (var replay = 0; replay < 2; replay++)
            {
                CollectionAssert.AreEqual(new[] { BulkItemOutcome.Succeeded, BulkItemOutcome.Succeeded },
                    await sink.WriteBatchAsync(batchWrites, default));
                foreach (var write in batchWrites)
                {
                    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                        Convert.ToBase64String(Encoding.UTF8.GetBytes("elastic:" + TestPassword)));
                    using var document = await http.GetAsync(new Uri(esUri, indexName + "/_doc/" + write.Record.LogEventId));
                    Assert.AreEqual(HttpStatusCode.OK, document.StatusCode);
                    using var readBack = JsonDocument.Parse(await document.Content.ReadAsStringAsync());
                    Assert.AreEqual(write.Record.LogEventId, readBack.RootElement.GetProperty("_source").GetProperty("LogEventId").GetString());
                    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", encodedKey);
                }
            }
            using var refresh = await http.PostAsync(new Uri(esUri, indexName + "/_refresh"), null);
            Assert.AreEqual(HttpStatusCode.Forbidden, refresh.StatusCode);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("elastic:" + TestPassword)));
            using var adminRefresh = await http.PostAsync(new Uri(esUri, indexName + "/_refresh"), null);
            Assert.AreEqual(HttpStatusCode.OK, adminRefresh.StatusCode);
            using var count = await http.GetAsync(new Uri(esUri, indexName + "/_count"));
            Assert.AreEqual(HttpStatusCode.OK, count.StatusCode);
            using var body = JsonDocument.Parse(await count.Content.ReadAsStringAsync());
            Assert.AreEqual(3L, body.RootElement.GetProperty("count").GetInt64());

            await using var kafka = await KafkaLogTlsFixture.StartAsync();
            var suffix = Guid.NewGuid().ToString("N");
            var source = "fullnet.integration.logging.es-tls-process." + suffix;
            var dlq = "fullnet.integration.logging.es-tls-dlq." + suffix;
            var group = "fullnet.integration.logging.es-tls-group." + suffix;
            await kafka.EnsureTopicsAsync(source, dlq);
            var processId = Guid.CreateVersion7().ToString("D");
            var processJson = $$"""
                {"@t":"{{occurred:O}}","@mt":"tls-process","LogEventId":"{{processId}}","log.class":"diagnostic","OccurredAtUtc":"{{occurred:O}}","ExpiresAtUtc":"{{expiry:O}}","IndexRouteVersion":2}
                """;
            using var producer = new ProducerBuilder<string, byte[]>(new ProducerConfig
            {
                BootstrapServers = kafka.BootstrapServers,
                SecurityProtocol = SecurityProtocol.Ssl,
                SslCaLocation = kafka.CaPath,
                Acks = Acks.All,
            }).Build();
            await producer.ProduceAsync(source, new Message<string, byte[]>
            {
                Key = processId,
                Value = Encoding.UTF8.GetBytes(processJson),
            });
            var hostDll = Path.Combine(AppContext.BaseDirectory, "Full.NET.Host.LogConsumer.dll");
            Assert.IsTrue(File.Exists(hostDll));
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.ArgumentList.Add(hostDll);
            start.Environment["FULLNET_LOG_CONSUMER_EXPERIMENTAL"] = "true";
            start.Environment["FULLNET_LOG_CONSUMER_BATCH_MAX_RECORDS"] = "8";
            start.Environment["FULLNET_LOG_CONSUMER_BOOTSTRAP_SERVERS"] = kafka.BootstrapServers;
            start.Environment["FULLNET_LOG_CONSUMER_TOPICS"] = source;
            start.Environment["FULLNET_LOG_CONSUMER_GROUP_ID"] = group;
            start.Environment["FULLNET_LOG_CONSUMER_DLQ_TOPIC"] = dlq;
            start.Environment["FULLNET_LOG_CONSUMER_ROUTES"] = "2:30";
            start.Environment["FULLNET_LOG_CONSUMER_MAX_EVENT_BYTES"] = "1024";
            start.Environment["FULLNET_LOG_CONSUMER_ES_URL"] = esUri.ToString();
            start.Environment["FULLNET_LOG_CONSUMER_ES_API_KEY"] = "invalid";
            start.Environment["FULLNET_LOG_CONSUMER_ES_CA_PATH"] = caPath;
            start.Environment["FULLNET_LOG_CONSUMER_ES_REVOCATION_MODE"] = "NoCheck";
            start.Environment["DOTNET_ENVIRONMENT"] = "Development";
            start.Environment["FULLNET_LOG_CONSUMER_SECURITY_PROTOCOL"] = "Ssl";
            start.Environment["FULLNET_LOG_CONSUMER_SSL_CA_LOCATION"] = kafka.CaPath;
            using (var failedProcess = Process.Start(start))
            {
                Assert.IsNotNull(failedProcess);
                var failedOutput = failedProcess.StandardOutput.ReadToEndAsync();
                var failedErrors = failedProcess.StandardError.ReadToEndAsync();
                try
                {
                    await failedProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));
                    Assert.AreEqual(3, failedProcess.ExitCode,
                        "无效 ES API Key 必须延期投递，保留来源位点。");
                    StringAssert.Contains(await failedOutput, "Log consumer started");
                    StringAssert.Contains(await failedErrors, "Log delivery deferred");
                }
                finally
                {
                    if (!failedProcess.HasExited) failedProcess.Kill(entireProcessTree: true);
                    await failedProcess.WaitForExitAsync();
                }
            }
            using (var beforeRestart = new ConsumerBuilder<byte[], byte[]>(new ConsumerConfig
                   {
                       BootstrapServers = kafka.BootstrapServers,
                       SecurityProtocol = SecurityProtocol.Ssl,
                       SslCaLocation = kafka.CaPath,
                       GroupId = group,
                       EnableAutoCommit = false,
                   }).Build())
            {
                var offset = Offset.Unset;
                var queried = false;
                var offsetDeadline = DateTimeOffset.UtcNow.AddSeconds(15);
                while (DateTimeOffset.UtcNow < offsetDeadline)
                {
                    try
                    {
                        offset = beforeRestart.Committed(
                            [new TopicPartition(source, new Partition(0))],
                            TimeSpan.FromSeconds(5))[0].Offset;
                        queried = true;
                        break;
                    }
                    catch (KafkaException error) when (error.Error.Code is
                        ErrorCode.GroupLoadInProgress or ErrorCode.GroupCoordinatorNotAvailable
                        or ErrorCode.NotCoordinatorForGroup)
                    {
                        await Task.Delay(250);
                    }
                }
                Assert.IsTrue(queried, "未能向 Kafka 查询来源 Consumer Group 位点。");
                Assert.IsTrue(offset.Value < 0,
                    "无效 ES API Key 不得提交来源 Offset。");
            }
            using (var rejectedDocument = await http.GetAsync(new Uri(esUri, indexName + "/_doc/" + processId)))
            {
                Assert.AreEqual(HttpStatusCode.NotFound, rejectedDocument.StatusCode);
            }
            start.Environment["FULLNET_LOG_CONSUMER_ES_API_KEY"] = encodedKey;
            using var process = Process.Start(start);
            Task<string>? processOutput = null;
            Task<string>? processErrors = null;
            try
            {
                Assert.IsNotNull(process);
                processOutput = process.StandardOutput.ReadToEndAsync();
                processErrors = process.StandardError.ReadToEndAsync();
                using var offsetReader = new ConsumerBuilder<byte[], byte[]>(new ConsumerConfig
                {
                    BootstrapServers = kafka.BootstrapServers,
                    SecurityProtocol = SecurityProtocol.Ssl,
                    SslCaLocation = kafka.CaPath,
                    GroupId = group,
                    EnableAutoCommit = false,
                }).Build();
                var commitDeadline = DateTimeOffset.UtcNow.AddSeconds(30);
                var committed = Offset.Unset;
                while (DateTimeOffset.UtcNow < commitDeadline && !process.HasExited)
                {
                    try
                    {
                        committed = offsetReader.Committed(
                            [new TopicPartition(source, new Partition(0))],
                            TimeSpan.FromSeconds(5))[0].Offset;
                    }
                    catch (KafkaException error) when (error.Error.Code is
                        ErrorCode.GroupLoadInProgress or ErrorCode.GroupCoordinatorNotAvailable
                        or ErrorCode.NotCoordinatorForGroup)
                    {
                        // 新建 Group 的协调器可能尚在迁移，不能把查询失败当作投递结果。
                    }
                    if (committed.Value == 1) break;
                    await Task.Delay(250);
                }
                Assert.AreEqual(1L, committed.Value,
                    "独立进程应通过私有 CA 与 API Key 写入 ES 后再提交 Offset。");
            }
            finally
            {
                if (process is not null)
                {
                    try
                    {
                        if (!process.HasExited) process.Kill(entireProcessTree: true);
                    }
                    catch (InvalidOperationException)
                    {
                        // 进程可能恰在检查后退出，仍须等待输出流结束。
                    }
                    await process.WaitForExitAsync();
                    if (processOutput is not null) _ = await processOutput;
                    if (processErrors is not null) _ = await processErrors;
                }
            }
            using var processRefresh = await http.PostAsync(new Uri(esUri, indexName + "/_refresh"), null);
            Assert.AreEqual(HttpStatusCode.OK, processRefresh.StatusCode);
            using var processDocument = await http.GetAsync(new Uri(esUri, indexName + "/_doc/" + processId));
            Assert.AreEqual(HttpStatusCode.OK, processDocument.StatusCode);

            var crashId = Guid.CreateVersion7().ToString("D");
            var crashJson = $$"""
                {"@t":"{{occurred:O}}","@mt":"pre-ack-crash","LogEventId":"{{crashId}}","log.class":"diagnostic","OccurredAtUtc":"{{occurred:O}}","ExpiresAtUtc":"{{expiry:O}}","IndexRouteVersion":2}
                """;
            await producer.ProduceAsync(source, new Message<string, byte[]>
            {
                Key = crashId,
                Value = Encoding.UTF8.GetBytes(crashJson),
            });
            using var ephemeralProxyCertificate = server.CopyWithPrivateKey(serverKey);
            // Windows Schannel 需要可取得的导入私钥句柄；不将测试私钥长期持久化。
            using var proxyCertificate = X509CertificateLoader.LoadPkcs12(
                ephemeralProxyCertificate.Export(X509ContentType.Pkcs12, TestPassword),
                TestPassword, X509KeyStorageFlags.UserKeySet);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", encodedKey);
            await using (var proxy = new PausedElasticsearchBulkProxy(proxyCertificate, http, esUri))
            {
                start.Environment["FULLNET_LOG_CONSUMER_ES_URL"] =
                    $"https://127.0.0.1:{proxy.Port}/";
                using var interrupted = Process.Start(start);
                Assert.IsNotNull(interrupted);
                var interruptedOutput = interrupted.StandardOutput.ReadToEndAsync();
                var interruptedErrors = interrupted.StandardError.ReadToEndAsync();
                try
                {
                    var forwarded = await proxy.Forwarded.WaitAsync(TimeSpan.FromSeconds(60));
                    Assert.AreEqual(HttpStatusCode.OK, forwarded.Status);
                    using var bulk = JsonDocument.Parse(forwarded.Body);
                    Assert.IsFalse(bulk.RootElement.GetProperty("errors").GetBoolean());
                    Assert.IsFalse(interrupted.HasExited,
                        "代理尚未返回 ES 回执时 Consumer 必须仍在等待。");
                    interrupted.Kill(entireProcessTree: true);
                    await interrupted.WaitForExitAsync();
                    StringAssert.Contains(await interruptedOutput, "Log consumer started");
                    _ = await interruptedErrors;
                }
                finally
                {
                    if (!interrupted.HasExited) interrupted.Kill(entireProcessTree: true);
                    await interrupted.WaitForExitAsync();
                }
            }
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("elastic:" + TestPassword)));
            using (var afterKill = new ConsumerBuilder<byte[], byte[]>(new ConsumerConfig
                   {
                       BootstrapServers = kafka.BootstrapServers,
                       SecurityProtocol = SecurityProtocol.Ssl,
                       SslCaLocation = kafka.CaPath,
                       GroupId = group,
                       EnableAutoCommit = false,
                   }).Build())
            {
                var offset = afterKill.Committed(
                    [new TopicPartition(source, new Partition(0))],
                    TimeSpan.FromSeconds(5))[0].Offset;
                Assert.AreEqual(1L, offset.Value,
                    "ES 已写入但回执未交给 Consumer 时 SIGKILL 不得提交 Offset 2。");
            }
            using var crashRefresh = await http.PostAsync(new Uri(esUri, indexName + "/_refresh"), null);
            Assert.AreEqual(HttpStatusCode.OK, crashRefresh.StatusCode);
            using var crashDocument = await http.GetAsync(new Uri(esUri, indexName + "/_doc/" + crashId));
            Assert.AreEqual(HttpStatusCode.OK, crashDocument.StatusCode,
                "SIGKILL 前真实 ES 应已写入该文档。");
            using var crashDocumentBody = JsonDocument.Parse(await crashDocument.Content.ReadAsStringAsync());
            var versionBeforeReplay = crashDocumentBody.RootElement.GetProperty("_version").GetInt64();

            start.Environment["FULLNET_LOG_CONSUMER_ES_URL"] = esUri.ToString();
            using var resumed = Process.Start(start);
            Assert.IsNotNull(resumed);
            var resumedOutput = resumed.StandardOutput.ReadToEndAsync();
            var resumedErrors = resumed.StandardError.ReadToEndAsync();
            try
            {
                using var replayReader = new ConsumerBuilder<byte[], byte[]>(new ConsumerConfig
                {
                    BootstrapServers = kafka.BootstrapServers,
                    SecurityProtocol = SecurityProtocol.Ssl,
                    SslCaLocation = kafka.CaPath,
                    GroupId = group,
                    EnableAutoCommit = false,
                }).Build();
                // SIGKILL 不会主动 LeaveGroup；覆盖 SDK 默认 45 秒会话失效及重新分配。
                var replayDeadline = DateTimeOffset.UtcNow.AddSeconds(90);
                var replayOffset = Offset.Unset;
                while (DateTimeOffset.UtcNow < replayDeadline && !resumed.HasExited)
                {
                    try
                    {
                        replayOffset = replayReader.Committed(
                            [new TopicPartition(source, new Partition(0))],
                            TimeSpan.FromSeconds(5))[0].Offset;
                    }
                    catch (KafkaException error) when (error.Error.Code is
                        ErrorCode.GroupLoadInProgress or ErrorCode.GroupCoordinatorNotAvailable
                        or ErrorCode.NotCoordinatorForGroup)
                    {
                    }
                    if (replayOffset.Value == 2) break;
                    await Task.Delay(250);
                }
                var replayState = resumed.HasExited
                    ? $"进程退出码 {resumed.ExitCode}；错误类型：{await resumedErrors}"
                    : "进程仍在运行，可能等待 Group 重新分配分区";
                Assert.AreEqual(2L, replayOffset.Value,
                    $"重启后必须重放原记录并提交 Offset 2；{replayState}。");

                const int probeCount = 32;
                var probeBytes = 0L;
                resumed.Refresh();
                var cpuBeforeProbe = resumed.TotalProcessorTime;
                var probeClock = Stopwatch.StartNew();
                for (var i = 0; i < probeCount; i++)
                {
                    var probeId = Guid.CreateVersion7().ToString("D");
                    var probeJson = $$"""
                        {"@t":"{{occurred:O}}","@mt":"local-drain","LogEventId":"{{probeId}}","log.class":"diagnostic","OccurredAtUtc":"{{occurred:O}}","ExpiresAtUtc":"{{expiry:O}}","IndexRouteVersion":2}
                        """;
                    var probeValue = Encoding.UTF8.GetBytes(probeJson);
                    probeBytes += probeValue.Length + Encoding.UTF8.GetByteCount(probeId);
                    await producer.ProduceAsync(source, new Message<string, byte[]>
                    {
                        Key = probeId,
                        Value = probeValue,
                    });
                }
                var probeDeadline = DateTimeOffset.UtcNow.AddSeconds(45);
                var probeOffset = Offset.Unset;
                while (DateTimeOffset.UtcNow < probeDeadline && !resumed.HasExited)
                {
                    probeOffset = replayReader.Committed(
                        [new TopicPartition(source, new Partition(0))],
                        TimeSpan.FromSeconds(5))[0].Offset;
                    if (probeOffset.Value == 2 + probeCount) break;
                    await Task.Delay(100);
                }
                probeClock.Stop();
                Assert.AreEqual(2L + probeCount, probeOffset.Value,
                    "轻量探针全部记录均须完成 ES 写入与来源位点提交。");
                resumed.Refresh();
                var probeSeconds = probeClock.Elapsed.TotalSeconds;
                Console.WriteLine($"log-consumer-local-probe records={probeCount} "
                    + $"wireBytes={probeBytes} elapsedSeconds={probeSeconds:F3} "
                    + $"eventsPerSecond={probeCount / probeSeconds:F1} "
                    + $"wireBytesPerSecond={probeBytes / probeSeconds:F0} "
                    + $"consumerCpuMilliseconds={(resumed.TotalProcessorTime - cpuBeforeProbe).TotalMilliseconds:F0} "
                    + $"consumerPeakWorkingSetBytes={resumed.PeakWorkingSet64}");
            }
            finally
            {
                if (!resumed.HasExited) resumed.Kill(entireProcessTree: true);
                await resumed.WaitForExitAsync();
                _ = await resumedOutput;
                _ = await resumedErrors;
            }
            using var replayRefresh = await http.PostAsync(new Uri(esUri, indexName + "/_refresh"), null);
            Assert.AreEqual(HttpStatusCode.OK, replayRefresh.StatusCode);
            using var replayCount = await http.GetAsync(new Uri(esUri, indexName + "/_count"));
            using var replayCountBody = JsonDocument.Parse(await replayCount.Content.ReadAsStringAsync());
            Assert.AreEqual(37L, replayCountBody.RootElement.GetProperty("count").GetInt64(),
                "同一 LogEventId 重放后应仅保留一份文档。");
            using var replayDocument = await http.GetAsync(new Uri(esUri, indexName + "/_doc/" + crashId));
            Assert.AreEqual(HttpStatusCode.OK, replayDocument.StatusCode);
            using var replayDocumentBody = JsonDocument.Parse(await replayDocument.Content.ReadAsStringAsync());
            Assert.AreEqual(versionBeforeReplay + 1,
                replayDocumentBody.RootElement.GetProperty("_version").GetInt64(),
                "重启必须对同一文档 ID 再次执行 ES 写入，不能只推进 Offset。");

            start.Environment["DOTNET_ENVIRONMENT"] = "Production";
            await AssertRejectedEnvironmentAsync(start, "Production 不得关闭 ES 证书吊销检查。");

            start.Environment.Remove("DOTNET_ENVIRONMENT");
            await AssertRejectedEnvironmentAsync(start, "未指定环境时也不得关闭 ES 证书吊销检查。");
        }
        finally
        {
            File.Delete(caPath);
        }
    }

    private static async Task AssertRejectedEnvironmentAsync(ProcessStartInfo start, string reason)
    {
        using var process = Process.Start(start);
        Assert.IsNotNull(process);
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(1, process.ExitCode, reason);
            StringAssert.Contains(await errors, "ArgumentException");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            _ = await output;
            _ = await errors;
        }
    }
}
