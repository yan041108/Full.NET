using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>固定 Fluent Bit 的 CRI/可信标签过滤和 Kafka 出口实验；不替换默认 Forward 部署。</summary>
internal sealed class KafkaRequestCollectorFixture : IAsyncDisposable
{
    private readonly IContainer _container;
    private readonly string _directory;
    private KafkaRequestCollectorFixture(IContainer container, string directory) { _container = container; _directory = directory; }

    public static async Task<KafkaRequestCollectorFixture> StartAsync(string root, string directory,
        KafkaLogTlsFixture kafka, string general, string priority, IReadOnlyList<string> source, CancellationToken token,
        string? liveCriDirectory = null)
    {
        var values = await File.ReadAllTextAsync(Path.Combine(root, "deploy/observability/fluent-bit-values.yaml"), token);
        string Block(string name)
        {
            var match = Regex.Match(values, $@"^  {name}: \|\r?\n([\s\S]*?)(?=^  [A-Za-z]+: \||^extraVolumes:|\z)", RegexOptions.Multiline);
            Assert.IsTrue(match.Success);
            return Regex.Replace(match.Groups[1].Value, "^    ", "", RegexOptions.Multiline).TrimEnd();
        }
        var service = Block("service").Replace("/fluent-bit/etc/conf/custom_parsers.conf", "/work/custom_parsers.conf")
            .Replace("/var/fluent-bit/buffer", "/work/storage");
        var inputs = Block("inputs").Replace("/var/fluent-bit/tail.db", "/work/storage/tail.db\n    Read_From_Head On")
            .Replace("Refresh_Interval  5", "Refresh_Interval  1");
        var filters = Block("filters").Replace("Kube_Tag_Prefix     kube.var.log.containers.",
            "Kube_Tag_Prefix     kube.var.log.containers.\n    Kube_Meta_Preload_Cache_Dir /work/meta\n    Kube_URL http://127.0.0.1:1\n    Kube_Token_File /work/token");
        Assert.IsTrue(filters.Contains("Kube_Meta_Preload_Cache_Dir", StringComparison.Ordinal));
        string Output(string lane, string topic, string retries, string storage) => $"""
            [OUTPUT]
                Name kafka
                Alias fullnet_{lane}_kafka
                Match fullnet.{lane}.*
                Brokers localhost:9096
                Topics {topic}
                Format json
                Message_Key_Field LogEventId
                Timestamp_Key collector.timestamp
                Timestamp_Format iso8601_ns
                Queue_Full_Retries 10
                Retry_Limit {retries}
                storage.total_limit_size {storage}
                rdkafka.security.protocol ssl
                rdkafka.ssl.ca.location /work/kafka-ca.crt
                rdkafka.enable.ssl.certificate.verification true
                rdkafka.ssl.endpoint.identification.algorithm https
                rdkafka.request.required.acks -1
                rdkafka.enable.idempotence true
                rdkafka.message.timeout.ms 15000
                rdkafka.queue.buffering.max.kbytes 32768
                rdkafka.queue.buffering.max.messages 10000

            """;
        var config = service + "\n\n" + inputs + "\n\n" + filters + "\n\n" + Output("priority", priority, "False", "256MB")
            + "\n" + Output("b2", general, "3", "512MB");
        var builder = new ContainerBuilder("cr.fluentbit.io/fluent/fluent-bit:4.1.1@sha256:2a5cb41f99b7c5f3386bb34d42ce3b19eb47fbef6d93b64c0bd4afe9e6ec389c")
            // 与测试 Broker 共享网络，仅在其额外 SSL 监听上连接，不依赖 Docker Desktop host 网络开关。
            .WithCreateParameterModifier(parameters => (parameters.HostConfig ??= new()).NetworkMode = "container:" + kafka.ContainerId)
            .WithResourceMapping(Encoding.UTF8.GetBytes(config), "/work/fluent-bit.conf")
            .WithResourceMapping(Encoding.UTF8.GetBytes(Block("customParsers")), "/work/custom_parsers.conf")
            .WithResourceMapping(await File.ReadAllBytesAsync(kafka.CaPath, token), "/work/kafka-ca.crt")
            .WithResourceMapping(Encoding.UTF8.GetBytes("local-test-only"), "/work/token")
            .WithCommand("-c", "/work/fluent-bit.conf");
        foreach (var (pod, ingress) in new[] { ("fullnet-collector", "collector"), ("fullnet-mirror", "applicationkafka") })
        {
            var cri = new StringBuilder();
            foreach (var line in source)
            {
                var json = JsonNode.Parse(line)!.AsObject();
                // 正文伪造标签不得改变可信 Pod 路由；对应镜像只能被整体排除。
                if (ingress == "applicationkafka")
                {
                    json["kubernetes"] = JsonNode.Parse("{\"labels\":{\"fullnet.io/log-ingress\":\"collector\"}}");
                    // 唯一镜像 ID 使误投不能冒充原始来源，即使正文元数据随后被移除。
                    json["LogEventId"] = Guid.CreateVersion7().ToString();
                    if (json.ContainsKey("RequestId")) json["RequestId"] = "mirror-" + json["RequestId"]!.GetValue<string>();
                }
                cri.Append(DateTimeOffset.UtcNow.ToString("O")).Append(" stdout F ").Append(json.ToJsonString()).Append('\n');
            }
            var meta = JsonSerializer.Serialize(new
            {
                apiVersion = "v1", kind = "Pod",
                metadata = new { name = pod, @namespace = "default", uid = pod, labels = new Dictionary<string, string> { ["fullnet.io/log-ingress"] = ingress } },
                spec = new { nodeName = "test-node" },
            });
            if (liveCriDirectory is null)
                builder = builder.WithResourceMapping(Encoding.UTF8.GetBytes(cri.ToString()), $"/var/log/containers/{LiveCollectorCriBridge.PodFile(pod)}");
            else
            {
                Directory.CreateDirectory(liveCriDirectory);
                await File.WriteAllTextAsync(Path.Combine(liveCriDirectory, LiveCollectorCriBridge.PodFile(pod)), "", token);
            }
            builder = builder.WithResourceMapping(Encoding.UTF8.GetBytes(meta), $"/work/meta/default_{pod}.meta");
        }
        if (liveCriDirectory is not null) builder = builder.WithBindMount(liveCriDirectory, "/var/log/containers");
        // 目录须在 Fluent Bit 启动前存在；放入空占位文件避免运行时写入路径丢失。
        var container = builder.WithResourceMapping(Array.Empty<byte>(), "/work/storage/.keep")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("stream processor started")).Build();
        await File.WriteAllTextAsync(Path.Combine(directory, "collector.conf"), config, token);
        try { await container.StartAsync(token); return new(container, directory); }
        catch { await container.DisposeAsync(); throw; }
    }

    public async Task<bool> VerifyInputDrainedAsync(Uri metricsUri, int sourceCount, CancellationToken token)
    {
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };
        var metrics = await client.GetStringAsync(new Uri(metricsUri, "api/v1/metrics/prometheus"), token);
        long Metric(string name, string alias)
        {
            var match = Regex.Match(metrics, $"^{name}\\{{name=\"{alias}\"\\}}\\s+(\\d+)", RegexOptions.Multiline);
            Assert.IsTrue(match.Success, "缺少实际采集指标：" + name + "/" + alias);
            return long.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        }
        var input = Metric("fluentbit_input_records_total", "tail.0");
        var output = Metric("fluentbit_output_proc_records_total", "fullnet_priority_kafka")
            + Metric("fluentbit_output_proc_records_total", "fullnet_b2_kafka");
        Assert.IsTrue(input <= sourceCount * 2L && output <= sourceCount, "采集器出现额外输入、重复或镜像输出。");
        if (input != sourceCount * 2L || output != sourceCount) return false;
        await File.WriteAllTextAsync(Path.Combine(_directory, "collector.metrics.prom"), metrics, token);
        return true;
    }

    public async Task StopAsync(CancellationToken token)
    {
        await _container.StopAsync(token);
        Assert.AreEqual(0L, await _container.GetExitCodeAsync(token), "采集器必须完成正常退出，不能用强制终止隐藏尾部镜像。");
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            var logs = await _container.GetLogsAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await File.WriteAllTextAsync(Path.Combine(_directory, "collector.log"), logs.Stdout + logs.Stderr);
        }
        finally { await _container.DisposeAsync(); }
    }
}
