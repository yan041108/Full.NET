using Full.NET.Messaging.Kafka;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>
/// CDC E2E 测试共用的 Connector 注册与健康检查。
/// </summary>
internal static class CdcDebeziumConnectorTestSupport
{
    internal static async Task RegisterHealthyShadowConnectorAsync(
        KafkaConnectAdminClient connectAdmin,
        string connectorName,
        IReadOnlyDictionary<string, string> connectorConfig,
        TimeSpan timeout)
    {
        await connectAdmin.RegisterConnectorAsync(connectorName, connectorConfig);
        if (!await connectAdmin.WaitForConnectorHealthyAsync(connectorName, timeout))
        {
            var status = await connectAdmin.TryGetConnectorStatusAsync(connectorName);
            Assert.Inconclusive(
                "Debezium connector task did not reach healthy RUNNING state. "
                + $"Connector status: {status}");
        }
    }
}
