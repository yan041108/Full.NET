using Full.NET.IntegrationTests.Messaging;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>独立日志角色真实 ELF 的门禁、TLS、确认后提交及崩溃重放；控制器自身仍是 JIT。</summary>
[TestClass, DoNotParallelize]
public sealed class NativeLogConsumerE2ETests
{
    [TestMethod]
    [DataRow(1)]
    [DataRow(64)]
    public async Task Native_log_consumer_requires_tls_and_api_key_and_replays_after_unacknowledged_write(int batchMaxRecords)
    {
        var executable = NativeIndependentHostProcess.RequireArtifact("log-consumer");
        await KafkaLogConsumerElasticsearchTlsTests.VerifyAsync(executable, batchMaxRecords);
        if (batchMaxRecords == 64)
        {
            await KafkaLogConsumerElasticsearchTlsTests.VerifyAsync(executable, batchMaxRecords, Confluent.Kafka.SaslMechanism.ScramSha256);
            await KafkaLogConsumerElasticsearchTlsTests.VerifyAsync(executable, batchMaxRecords, Confluent.Kafka.SaslMechanism.ScramSha512);
        }
    }

    [TestMethod]
    public Task Native_log_consumer_does_not_commit_failed_dlq_and_replays_then_stops_cleanly() =>
        KafkaLogConsumerDeliveryTests.VerifyIndependentReplayAsync(NativeIndependentHostProcess.RequireArtifact("log-consumer"));

    [TestMethod]
    public async Task Native_log_consumer_gate_closes_before_missing_network_configuration()
    {
        var executable = NativeIndependentHostProcess.RequireArtifact("log-consumer");
        await using var process = NativeIndependentHostProcess.Start(executable, [], new Dictionary<string, string?>
        {
            ["FULLNET_LOG_CONSUMER_EXPERIMENTAL"] = "false", ["FULLNET_LOG_CONSUMER_BOOTSTRAP_SERVERS"] = null,
            ["FULLNET_LOG_CONSUMER_ES_API_KEY"] = null, ["FULLNET_LOG_CONSUMER_ES_URL"] = null
        }, "log-consumer-closed-gate");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var result = await process.WaitAsync(timeout.Token);
        Assert.AreEqual(2, result.ExitCode);
        StringAssert.Contains(result.Errors, "Log consumer experimental gate is closed.");
        Assert.IsFalse(result.Output.Contains("Log consumer started", StringComparison.Ordinal));
    }
}
