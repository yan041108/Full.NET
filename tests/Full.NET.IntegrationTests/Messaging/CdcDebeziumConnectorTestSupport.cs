using Full.NET.Messaging.Kafka;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>
/// CDC E2E 测试共用的 Connector 注册与健康检查。
/// </summary>
internal static class CdcDebeziumConnectorTestSupport
{
    /// <summary>
    /// 注册使用独立 offset 身份的影子 Connector，并等待其任务进入健康状态。
    /// </summary>
    /// <param name="connectAdmin">Kafka Connect 管理客户端。</param>
    /// <param name="connectorName">本次测试唯一的 Connector 名称。</param>
    /// <param name="connectorConfig">由冻结模板生成的 Connector 配置。</param>
    /// <param name="timeout">等待 Connector 健康的最长时间。</param>
    internal static async Task RegisterHealthyShadowConnectorAsync(
        KafkaConnectAdminClient connectAdmin,
        string connectorName,
        IReadOnlyDictionary<string, string> connectorConfig,
        TimeSpan timeout)
    {
        var isolatedConfig = CreateIsolatedConnectorConfig(connectorName, connectorConfig);
        await connectAdmin.RegisterConnectorAsync(connectorName, isolatedConfig);
        if (!await connectAdmin.WaitForConnectorHealthyAsync(connectorName, timeout))
        {
            var status = await connectAdmin.TryGetConnectorStatusAsync(connectorName);
            Assert.Inconclusive(
                "Debezium connector task did not reach healthy RUNNING state. "
                + $"Connector status: {status}");
        }

        if (!await WaitForConnectorPositionAsync(connectAdmin, connectorName, timeout))
        {
            var status = await connectAdmin.TryGetConnectorStatusAsync(connectorName);
            Assert.Inconclusive(
                "Debezium connector did not publish an initial source position within timeout. "
                + $"Connector status: {status}");
        }
    }

    /// <summary>
    /// 为动态测试 Connector 创建独立的 Debezium offset 身份。
    /// </summary>
    /// <param name="connectorName">本次测试唯一的 Connector 名称。</param>
    /// <param name="connectorConfig">共享模板配置。</param>
    /// <returns>不会与其他测试 Connector 复用 offset 的配置副本。</returns>
    internal static IReadOnlyDictionary<string, string> CreateIsolatedConnectorConfig(
        string connectorName,
        IReadOnlyDictionary<string, string> connectorConfig)
    {
        var isolatedConfig = new Dictionary<string, string>(connectorConfig, StringComparer.Ordinal);
        if (isolatedConfig.TryGetValue("topic.prefix", out var topicPrefix))
        {
            // Debezium 以 topic.prefix 标识源分区；动态数据库若共用该值会错误继承其他测试的 binlog offset。
            isolatedConfig["topic.prefix"] = $"{topicPrefix}.{connectorName}";
        }

        if (isolatedConfig.TryGetValue("connector.class", out var connectorClass)
            && connectorClass.EndsWith(".mysql.MySqlConnector", StringComparison.Ordinal))
        {
            // 测试库在注册前已完成迁移，无需快照元数据锁；避免 RUNNING 状态下初始化锁阻塞 Outbox 写入。
            isolatedConfig["snapshot.locking.mode"] = "none";
            // 每个并存的 MySQL 复制客户端必须使用不同 server ID，否则 Connector 会相互踢下线并遗漏 binlog 事件。
            isolatedConfig["database.server.id"] = CreateMySqlServerId(connectorName);
        }

        return isolatedConfig;
    }

    /// <summary>
    /// 等待 Connector 发布初始源位置，证明 no_data 快照已结束且后续写入不会落入初始化窗口。
    /// </summary>
    /// <param name="connectAdmin">Kafka Connect 管理客户端。</param>
    /// <param name="connectorName">需要检查的 Connector 名称。</param>
    /// <param name="timeout">等待初始位置的最长时间。</param>
    /// <returns>已观测到源位置时返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
    private static async Task<bool> WaitForConnectorPositionAsync(
        KafkaConnectAdminClient connectAdmin,
        string connectorName,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            if (await connectAdmin.TryReadConnectorPositionAsync(connectorName) is not null)
            {
                return true;
            }

            await Task.Delay(500);
        }

        return false;
    }

    /// <summary>
    /// 根据 Connector 名称生成稳定且非零的 MySQL 复制客户端 server ID。
    /// </summary>
    /// <param name="connectorName">本次测试唯一的 Connector 名称。</param>
    /// <returns>位于 MySQL 无符号 32 位合法范围内的十进制 server ID。</returns>
    private static string CreateMySqlServerId(string connectorName)
    {
        const uint fnvOffsetBasis = 2166136261;
        const uint fnvPrime = 16777619;
        var hash = fnvOffsetBasis;
        foreach (var character in connectorName)
        {
            hash ^= character;
            hash = unchecked(hash * fnvPrime);
        }

        var serverId = ((ulong)hash % uint.MaxValue) + 1;
        return serverId.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
