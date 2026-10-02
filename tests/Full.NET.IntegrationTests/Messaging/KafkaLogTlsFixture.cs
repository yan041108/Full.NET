using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>独立的单节点 TLS Kafka 夹具；证书只在测试进程和容器内存在。</summary>
internal sealed class KafkaLogTlsFixture : IAsyncDisposable
{
    private const string TestPassword = "fullnet-logging-test-only";
    private const string SaslUser = "log-writer";
    private const string SaslPasswordValue = "fullnet-log-writer-test-only";
    private const string AdminUser = "log-test-admin";
    private const string AdminPassword = "fullnet-log-admin-test-only";
    private readonly IContainer _container;
    private readonly string _caPath;
    private readonly bool _useSasl;
    private readonly bool _useAcls;

    private KafkaLogTlsFixture(IContainer container, string caPath, string bootstrapServers, bool useSasl, bool useAcls)
    {
        _container = container;
        _caPath = caPath;
        _useSasl = useSasl;
        _useAcls = useAcls;
        BootstrapServers = bootstrapServers;
    }

    public string BootstrapServers { get; }

    public string CaPath => _caPath;
    public string ContainerId => _container.Id;
    public Uri CollectorMetricsUri => new UriBuilder("http", _container.Hostname, _container.GetMappedPublicPort(2020)).Uri;

    public string? SaslUsername => _useSasl ? SaslUser : null;

    public string? SaslPassword => _useSasl ? SaslPasswordValue : null;

    public static async Task<KafkaLogTlsFixture> StartAsync(bool useSasl = false, bool useAcls = false, CancellationToken cancellationToken = default,
        bool enableCollectorListener = false)
    {
        if (useAcls && !useSasl)
        {
            throw new ArgumentException("ACL test requires SASL authentication.", nameof(useAcls));
        }
        if (enableCollectorListener && useSasl) throw new ArgumentException("本地共享网络采集实验仅使用 SSL。");
        using var caKey = RSA.Create(2048);
        var caRequest = new CertificateRequest(
            "CN=FullNET Logging Test CA", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        caRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        var validFrom = DateTimeOffset.UtcNow.AddMinutes(-5);
        var validUntil = validFrom.AddHours(1);
        using var ca = caRequest.CreateSelfSigned(validFrom, validUntil);

        var brokerHost = Environment.GetEnvironmentVariable("TESTCONTAINERS_HOST_OVERRIDE");
        brokerHost = string.IsNullOrWhiteSpace(brokerHost) ? "127.0.0.1" : brokerHost.Trim();

        using var serverKey = RSA.Create(2048);
        var serverRequest = new CertificateRequest(
            "CN=localhost", serverKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddDnsName("host.docker.internal");
        san.AddIpAddress(IPAddress.Loopback);
        if (IPAddress.TryParse(brokerHost, out var brokerAddress))
        {
            san.AddIpAddress(brokerAddress);
        }
        else if (!string.Equals(brokerHost, "localhost", StringComparison.OrdinalIgnoreCase)
                 && !string.Equals(brokerHost, "host.docker.internal", StringComparison.OrdinalIgnoreCase))
        {
            san.AddDnsName(brokerHost);
        }
        serverRequest.CertificateExtensions.Add(san.Build());
        serverRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        serverRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid("1.3.6.1.5.5.7.3.1")], true));
        // 服务端有效期严格包含于签发 CA，避免独立取当前时间造成 notAfter 越过 CA 终点。
        using var server = serverRequest.Create(
            ca, validFrom.AddMinutes(1), validUntil.AddMinutes(-1),
            RandomNumberGenerator.GetBytes(16));
        using var serverWithKey = server.CopyWithPrivateKey(serverKey);
        var caPath = Path.Combine(Path.GetTempPath(), $"fullnet-log-kafka-ca-{Guid.NewGuid():N}.crt");
        await File.WriteAllTextAsync(caPath, ca.ExportCertificatePem(), cancellationToken).ConfigureAwait(false);

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var hostPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        // 嵌套 Docker 测试进程须通过宿主网关访问映射端口；证书 SAN 与公布地址一致。
        var bootstrap = $"{brokerHost}:{hostPort}";
        var externalListener = useSasl ? "SASL_SSL" : "SSL";
        var containerBuilder = new ContainerBuilder("apache/kafka:4.1.2")
            .WithPortBinding(hostPort, 9093)
            .WithResourceMapping(serverWithKey.Export(X509ContentType.Pkcs12, TestPassword),
                "/etc/kafka/secrets/server.p12", 1000, 1000)
            .WithResourceMapping(Encoding.UTF8.GetBytes(TestPassword),
                "/etc/kafka/secrets/keystore.creds", 1000, 1000)
            .WithResourceMapping(Encoding.UTF8.GetBytes(TestPassword),
                "/etc/kafka/secrets/key.creds", 1000, 1000)
            .WithEnvironment("KAFKA_NODE_ID", "1")
            .WithEnvironment("KAFKA_PROCESS_ROLES", "broker,controller")
            .WithEnvironment("KAFKA_LISTENERS",
                $"INTERNAL://0.0.0.0:9092,{externalListener}://0.0.0.0:9093,CONTROLLER://0.0.0.0:9094" + (enableCollectorListener ? ",COLLECTORSSL://0.0.0.0:9096" : ""))
            .WithEnvironment("KAFKA_ADVERTISED_LISTENERS",
                $"INTERNAL://localhost:9092,{externalListener}://{bootstrap}" + (enableCollectorListener ? ",COLLECTORSSL://localhost:9096" : ""))
            .WithEnvironment("KAFKA_LISTENER_SECURITY_PROTOCOL_MAP",
                $"INTERNAL:PLAINTEXT,{externalListener}:{externalListener},CONTROLLER:PLAINTEXT" + (enableCollectorListener ? ",COLLECTORSSL:SSL" : ""))
            .WithEnvironment("KAFKA_INTER_BROKER_LISTENER_NAME", "INTERNAL")
            .WithEnvironment("KAFKA_CONTROLLER_LISTENER_NAMES", "CONTROLLER")
            .WithEnvironment("KAFKA_CONTROLLER_QUORUM_VOTERS", "1@localhost:9094")
            .WithEnvironment("KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR", "1")
            .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR", "1")
            .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_MIN_ISR", "1")
            .WithEnvironment("KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS", "0")
            .WithEnvironment("KAFKA_AUTO_CREATE_TOPICS_ENABLE", "false")
            .WithEnvironment("CLUSTER_ID", "fullnet-log-tls-kafka-cluster")
            .WithEnvironment("KAFKA_SSL_KEYSTORE_FILENAME", "server.p12")
            .WithEnvironment("KAFKA_SSL_KEYSTORE_CREDENTIALS", "keystore.creds")
            .WithEnvironment("KAFKA_SSL_KEY_CREDENTIALS", "key.creds")
            .WithEnvironment("KAFKA_SSL_KEYSTORE_TYPE", "PKCS12")
            .WithEnvironment("KAFKA_SSL_CLIENT_AUTH", "none");
        if (useSasl)
        {
            // JAAS 仅在测试容器内注册固定测试账号，避免把认证配置写入仓库或宿主环境变量。
            var jaas = $"KafkaServer {{ org.apache.kafka.common.security.plain.PlainLoginModule required "
                       + $"username=\"broker\" password=\"{TestPassword}\" "
                       + $"user_{SaslUser}=\"{SaslPasswordValue}\" "
                       + $"user_{AdminUser}=\"{AdminPassword}\"; }};";
            containerBuilder = containerBuilder
                .WithResourceMapping(Encoding.UTF8.GetBytes(jaas),
                    "/etc/kafka/secrets/broker_jaas.conf", 1000, 1000)
                .WithEnvironment("KAFKA_OPTS",
                    "-Djava.security.auth.login.config=/etc/kafka/secrets/broker_jaas.conf")
                .WithEnvironment("KAFKA_SASL_ENABLED_MECHANISMS", "PLAIN");
            if (useAcls)
            {
                // 内部明文监听仅供单节点复制和控制器使用；外部受限账号只能经 SASL_SSL 进入。
                containerBuilder = containerBuilder
                    .WithEnvironment("KAFKA_AUTHORIZER_CLASS_NAME",
                        "org.apache.kafka.metadata.authorizer.StandardAuthorizer")
                    .WithEnvironment("KAFKA_ALLOW_EVERYONE_IF_NO_ACL_FOUND", "false")
                    .WithEnvironment("KAFKA_SUPER_USERS", $"User:ANONYMOUS;User:{AdminUser}");
            }
        }

        if (enableCollectorListener) containerBuilder = containerBuilder.WithPortBinding(2020, assignRandomHostPort: true);
        var container = containerBuilder
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Kafka Server started"))
            .Build();
        try
        {
            await container.StartAsync(cancellationToken).ConfigureAwait(false);
            return new KafkaLogTlsFixture(container, caPath, bootstrap, useSasl, useAcls);
        }
        catch
        {
            await container.DisposeAsync().ConfigureAwait(false);
            File.Delete(caPath);
            throw;
        }
    }

    public Task EnsureTopicsAsync(params string[] topics) => EnsureTopicsAsync(CancellationToken.None, topics);

    public async Task EnsureTopicsAsync(CancellationToken cancellationToken, params string[] topics)
    {
        using var admin = CreateAdminClient();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await admin.CreateTopicsAsync(topics.Select(topic => new TopicSpecification
                {
                    Name = topic,
                    NumPartitions = 1,
                    ReplicationFactor = 1,
                }), new CreateTopicsOptions { RequestTimeout = TimeSpan.FromSeconds(10), OperationTimeout = TimeSpan.FromSeconds(10) })
                    .WaitAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (KafkaException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException("TLS Kafka topic readiness timed out.");
    }

    public async Task GrantWriterAsync(string topic)
    {
        if (!_useAcls)
        {
            throw new InvalidOperationException("ACL fixture was not enabled.");
        }

        using var admin = CreateAdminClient();
        var writer = $"User:{SaslUser}";
        await admin.CreateAclsAsync([
            Binding(ResourceType.Topic, topic, writer, AclOperation.Write),
            Binding(ResourceType.Topic, topic, writer, AclOperation.Describe),
        ]).ConfigureAwait(false);
    }

    public IConsumer<string, byte[]> CreateReadbackConsumer(string groupId)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = BootstrapServers,
            SecurityProtocol = _useSasl ? SecurityProtocol.SaslSsl : SecurityProtocol.Ssl,
            SslCaLocation = CaPath,
            GroupId = groupId,
            EnableAutoCommit = false,
        };
        if (_useSasl)
        {
            config.SaslMechanism = SaslMechanism.Plain;
            config.SaslUsername = _useAcls ? AdminUser : SaslUser;
            config.SaslPassword = _useAcls ? AdminPassword : SaslPasswordValue;
        }

        return new ConsumerBuilder<string, byte[]>(config).Build();
    }

    private static AclBinding Binding(ResourceType type, string name, string principal, AclOperation operation) => new()
    {
        Pattern = new ResourcePattern
        {
            Type = type,
            Name = name,
            ResourcePatternType = ResourcePatternType.Literal,
        },
        Entry = new AccessControlEntry
        {
            Principal = principal,
            Host = "*",
            Operation = operation,
            PermissionType = AclPermissionType.Allow,
        },
    };

    private IAdminClient CreateAdminClient()
    {
        var config = new AdminClientConfig
        {
            BootstrapServers = BootstrapServers,
            SecurityProtocol = _useSasl ? SecurityProtocol.SaslSsl : SecurityProtocol.Ssl,
            SslCaLocation = CaPath,
        };
        if (_useSasl)
        {
            config.SaslMechanism = SaslMechanism.Plain;
            config.SaslUsername = _useAcls ? AdminUser : SaslUser;
            config.SaslPassword = _useAcls ? AdminPassword : SaslPasswordValue;
        }

        return new AdminClientBuilder(config).Build();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            File.Delete(_caPath);
        }
    }
}
