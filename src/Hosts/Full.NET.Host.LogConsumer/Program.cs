using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Confluent.Kafka;
using Full.NET.LogConsumer;

return await RunAsync();

static async Task<int> RunAsync()
{
    // 实验门禁独立于应用宿主；没有显式启用时不创建网络客户端。
    if (Environment.GetEnvironmentVariable("FULLNET_LOG_CONSUMER_EXPERIMENTAL") != "true")
    {
        Console.Error.WriteLine("Log consumer experimental gate is closed.");
        return 2;
    }

    try
    {
        var portValue = Environment.GetEnvironmentVariable("FULLNET_LOG_CONSUMER_HEALTH_PORT") ?? "0";
        if (!int.TryParse(portValue, out var healthPort) || (healthPort != 0 && healthPort is < 1024 or > 65535))
            throw new ArgumentException("运维端口须为 0 或 1024..65535。");
        var runtime = new ConsumerRuntimeState();
        if (!int.TryParse(Environment.GetEnvironmentVariable("FULLNET_LOG_CONSUMER_BATCH_MAX_RECORDS") ?? "1", out var batchMaxRecords)
            || batchMaxRecords is < 1 or > 8)
            throw new ArgumentException("批次条数须为 1..8。");
        var bootstrap = Required("FULLNET_LOG_CONSUMER_BOOTSTRAP_SERVERS");
        var topics = Required("FULLNET_LOG_CONSUMER_TOPICS")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var group = Required("FULLNET_LOG_CONSUMER_GROUP_ID");
        var dlqTopic = Required("FULLNET_LOG_CONSUMER_DLQ_TOPIC");
        if (topics.Length is < 1 or > 8 || topics.Distinct(StringComparer.Ordinal).Count() != topics.Length
            || topics.Contains(dlqTopic, StringComparer.Ordinal))
        {
            throw new ArgumentException("Topic 配置无效。");
        }

        var routes = ParseRoutes(Required("FULLNET_LOG_CONSUMER_ROUTES"));

        var maxBytes = int.Parse(Required("FULLNET_LOG_CONSUMER_MAX_EVENT_BYTES"),
            System.Globalization.CultureInfo.InvariantCulture);
        if (maxBytes is < 256 or > 65536)
        {
            throw new ArgumentException("单事件字节上限超出范围。");
        }

        var esUrl = new Uri(Required("FULLNET_LOG_CONSUMER_ES_URL"));
        var apiKey = Required("FULLNET_LOG_CONSUMER_ES_API_KEY");
        var protocol = Required("FULLNET_LOG_CONSUMER_SECURITY_PROTOCOL") switch
        {
            "Ssl" => SecurityProtocol.Ssl,
            "SaslSsl" => SecurityProtocol.SaslSsl,
            _ => throw new ArgumentException("Kafka 仅允许 Ssl 或 SaslSsl。"),
        };
        var caLocation = Environment.GetEnvironmentVariable("FULLNET_LOG_CONSUMER_SSL_CA_LOCATION");
        var esCaPath = Environment.GetEnvironmentVariable("FULLNET_LOG_CONSUMER_ES_CA_PATH");
        var esRevocationSetting = Environment.GetEnvironmentVariable("FULLNET_LOG_CONSUMER_ES_REVOCATION_MODE");
        var esRevocationMode = esRevocationSetting switch
        {
            null or "" or "Online" => X509RevocationMode.Online,
            "NoCheck" when string.Equals(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"),
                "Development", StringComparison.OrdinalIgnoreCase) => X509RevocationMode.NoCheck,
            _ => throw new ArgumentException("ES 证书吊销模式无效；仅 Development 允许 NoCheck。"),
        };
        if (esRevocationSetting is not null && string.IsNullOrWhiteSpace(esCaPath))
        {
            throw new ArgumentException("ES 证书吊销模式要求配置 ES_CA_PATH。");
        }
        var mechanism = Environment.GetEnvironmentVariable("FULLNET_LOG_CONSUMER_SASL_MECHANISM");
        var username = Environment.GetEnvironmentVariable("FULLNET_LOG_CONSUMER_SASL_USERNAME");
        var password = Environment.GetEnvironmentVariable("FULLNET_LOG_CONSUMER_SASL_PASSWORD");
        if (protocol == SecurityProtocol.SaslSsl
            && (mechanism is not ("Plain" or "ScramSha256" or "ScramSha512")
                || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)))
        {
            throw new ArgumentException("SASL SSL 配置不完整。");
        }

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = bootstrap,
            GroupId = group,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            IsolationLevel = IsolationLevel.ReadCommitted,
            SecurityProtocol = protocol,
            SslCaLocation = caLocation,
            MaxPollIntervalMs = 300000,
            SocketTimeoutMs = 30000,
            StatisticsIntervalMs = 10000,
        };
        var dlqConfig = new ProducerConfig
        {
            BootstrapServers = bootstrap,
            SecurityProtocol = protocol,
            SslCaLocation = caLocation,
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageTimeoutMs = 30000,
        };
        if (protocol == SecurityProtocol.SaslSsl)
        {
            var parsed = Enum.Parse<SaslMechanism>(mechanism!, false);
            consumerConfig.SaslMechanism = parsed;
            consumerConfig.SaslUsername = username;
            consumerConfig.SaslPassword = password;
            dlqConfig.SaslMechanism = parsed;
            dlqConfig.SaslUsername = username;
            dlqConfig.SaslPassword = password;
        }

        long assignmentEpoch = 0;
        using var consumer = new ConsumerBuilder<byte[], byte[]>(consumerConfig)
            .SetPartitionsAssignedHandler((_, partitions) => { assignmentEpoch++; runtime.Assign(partitions.Select(p => (p.Topic, p.Partition.Value)).ToArray()); })
            .SetPartitionsRevokedHandler((_, _) => { assignmentEpoch++; runtime.Assign([]); })
            .SetPartitionsLostHandler((_, _) => { assignmentEpoch++; runtime.Assign([]); })
            .SetStatisticsHandler((_, json) => runtime.Statistics(json))
            .Build();
        using var dlqProducer = new ProducerBuilder<string, byte[]>(dlqConfig).Build();
        using var httpHandler = new SocketsHttpHandler { AllowAutoRedirect = false };
        if (!string.IsNullOrWhiteSpace(esCaPath))
        {
            var policy = new X509ChainPolicy
            {
                TrustMode = X509ChainTrustMode.CustomRootTrust,
                RevocationMode = esRevocationMode,
            };
            // CA Secret 仅包含公钥证书；CreateFromPemFile 会错误地要求同文件存在私钥。
            policy.CustomTrustStore.Add(X509CertificateLoader.LoadCertificateFromFile(esCaPath));
            httpHandler.SslOptions.CertificateChainPolicy = policy;
        }
        using var http = new HttpClient(httpHandler) { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", apiKey);
        var sink = new ElasticsearchLogDocumentSink(http, esUrl);
        var dlq = new ObservedLogDeadLetterSink(new KafkaLogDeadLetterSink(dlqProducer, dlqTopic), runtime);
        var committer = new ObservedLogOffsetCommitter(new KafkaLogOffsetCommitter(consumer), runtime);
        var processor = new SequentialLogDeliveryProcessor(new ObservedLogDocumentSink(sink, runtime), dlq, committer, routes, maxBytes);
        var batchProcessor = new BatchLogDeliveryProcessor(new ObservedLogDocumentBatchSink(sink, runtime), dlq, committer, routes, maxBytes);
        await using var operations = await ConsumerOperationsHost.StartAsync(healthPort, runtime);
        var stoppingToken = operations.StoppingToken;
        consumer.Subscribe(topics);
        Console.WriteLine("Log consumer started; Offset commits require ES item or DLQ broker confirmation.");
        try
        {
            ConsumeResult<byte[], byte[]>? pending = null;
            while (!stoppingToken.IsCancellationRequested)
            {
                // 短轮询让停机无需新消息唤醒；位点确认仍保持单循环串行，未确认记录不提交。
                var message = pending ?? consumer.Consume(TimeSpan.FromMilliseconds(250));
                pending = null;
                if (message is null || message.IsPartitionEOF) continue;
                var epoch = assignmentEpoch;
                var input = ToInput(message);
                var committed = 0;
                LogDeliveryDisposition disposition;
                if (batchMaxRecords == 1 || input.Value.Length > maxBytes || input.RawKey.Length > 36)
                {
                    disposition = await processor.ProcessAsync(input, stoppingToken);
                    committed = disposition == LogDeliveryDisposition.Completed ? 1 : 0;
                }
                else
                {
                    // 只取已可用消息，不等待凑批；异常大记录留给原单条隔离，限制本批次持有的原始字节。
                    var batch = new List<KafkaLogInput>(batchMaxRecords) { input };
                    while (batch.Count < batchMaxRecords)
                    {
                        var next = consumer.Consume(TimeSpan.Zero);
                        if (assignmentEpoch != epoch) { runtime.Deferred(); return 3; }
                        if (next is null || next.IsPartitionEOF) break;
                        var nextInput = ToInput(next);
                        if (nextInput.Value.Length > maxBytes || nextInput.RawKey.Length > 36) { pending = next; break; }
                        batch.Add(nextInput);
                    }
                    var result = await batchProcessor.ProcessAsync(batch, () => assignmentEpoch == epoch, stoppingToken);
                    disposition = result.Disposition;
                    committed = result.CommittedRecords;
                }
                for (var i = 0; i < committed; i++) runtime.Committed();
                if (disposition == LogDeliveryDisposition.Retry)
                {
                    runtime.Deferred();
                    // 不继续消费，交给进程监督器退避重启并重放原 Offset。
                    Console.Error.WriteLine("Log delivery deferred; current offset remains uncommitted.");
                    return 3;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return 0;
        }
        finally
        {
            runtime.Stop();
            consumer.Close();
        }

        return 0;
    }
    catch (Exception error)
    {
        // 原始事件、下游错误正文与连接配置可能含秘密，控制台仅输出异常类型。
        Console.Error.WriteLine("Log consumer stopped: " + error.GetType().Name);
        return 1;
    }
}

static KafkaLogInput ToInput(ConsumeResult<byte[], byte[]> message)
{
    var rawKey = message.Message.Key;
    return new(message.Topic, message.Partition.Value, message.Offset.Value,
        KafkaLogKeyDecoder.Decode(rawKey), message.Message.Value ?? [], rawKey ?? []);
}

static string Required(string name)
{
    var value = Environment.GetEnvironmentVariable(name);
    return string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException("缺少必需配置 " + name)
        : value;
}

static IReadOnlyDictionary<int, int> ParseRoutes(string specification)
{
    var routes = new Dictionary<int, int>();
    var entries = specification.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    if (entries.Length is < 1 or > 16) throw new ArgumentException("索引路由数量无效。");
    foreach (var entry in entries)
    {
        var fields = entry.Split(':', StringSplitOptions.TrimEntries);
        if (fields.Length != 2
            || !int.TryParse(fields[0], out var version)
            || !int.TryParse(fields[1], out var days)
            || version is < 1 or > 9999 || days is < 1 or > 3650
            || !routes.TryAdd(version, days))
        {
            throw new ArgumentException("索引路由映射无效。");
        }
    }
    return routes;
}

internal sealed class KafkaLogDeadLetterSink(
    IProducer<string, byte[]> producer, string topic) : ILogDeadLetterSink
{
    public async Task<bool> PublishAsync(KafkaLogInput input, LogIsolationReason reason,
        CancellationToken cancellationToken)
    {
        var message = new Message<string, byte[]>
        {
            Key = $"{input.Topic}:{input.Partition}:{input.Offset}",
            Value = input.Value.ToArray(),
            Headers =
            [
                new Header("source-topic", Encoding.UTF8.GetBytes(input.Topic)),
                new Header("source-partition", Encoding.UTF8.GetBytes(input.Partition.ToString(System.Globalization.CultureInfo.InvariantCulture))),
                new Header("source-offset", Encoding.UTF8.GetBytes(input.Offset.ToString(System.Globalization.CultureInfo.InvariantCulture))),
                new Header("source-key-bytes", input.RawKey.ToArray()),
                new Header("isolation-reason", Encoding.UTF8.GetBytes(reason.ToString())),
            ],
        };
        var result = await producer.ProduceAsync(topic, message, cancellationToken);
        return result.Status == PersistenceStatus.Persisted;
    }
}

internal static class KafkaLogKeyDecoder
{
    public static string? Decode(byte[]? bytes)
    {
        if (bytes is null) return null;
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }
}

internal sealed class KafkaLogOffsetCommitter(IConsumer<byte[], byte[]> consumer)
    : ILogOffsetCommitter
{
    public void CommitNext(KafkaLogInput input) =>
        consumer.Commit([new TopicPartitionOffset(input.Topic,
            new Partition(input.Partition), new Offset(checked(input.Offset + 1)))]);
}
