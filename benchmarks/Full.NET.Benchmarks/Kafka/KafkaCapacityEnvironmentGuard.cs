using Full.NET.Messaging.Kafka;

namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 表示 Kafka 集群身份校验所需的非敏感元数据。
/// </summary>
public sealed record KafkaCapacityClusterIdentity(
    string ClusterId,
    int BrokerCount);

/// <summary>
/// 表示容量计划或集群保护检查的稳定结果。
/// </summary>
public sealed record KafkaCapacityGuardResult(
    bool IsAllowed,
    string ReasonCode,
    string Message);

/// <summary>
/// 对独立 Kafka 容量运行器执行失败关闭的环境和集群保护。
/// </summary>
public static class KafkaCapacityEnvironmentGuard
{
    /// <summary>
    /// 在建立 Kafka 连接前验证配置、环境和显式执行许可。
    /// </summary>
    public static KafkaCapacityGuardResult ValidatePlan(
        KafkaCapacityConfiguration configuration,
        KafkaCapacityOptions options)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);

        if (!configuration.Kafka.Enabled)
        {
            return Rejected(
                "kafka_disabled",
                "Kafka capacity configuration must explicitly enable Kafka.");
        }

        if (string.IsNullOrWhiteSpace(configuration.Kafka.BootstrapServers))
        {
            return Rejected(
                "bootstrap_servers_required",
                "Kafka bootstrap servers must be provided through protected configuration.");
        }

        if (string.Equals(
                configuration.EnvironmentName,
                "Production",
                StringComparison.OrdinalIgnoreCase))
        {
            return Rejected(
                "production_forbidden",
                "Kafka capacity execution is forbidden in the Production environment.");
        }

        if (string.IsNullOrWhiteSpace(configuration.ExpectedClusterId))
        {
            return Rejected(
                "expected_cluster_id_required",
                "An expected Kafka Cluster Id must be configured before connecting.");
        }

        var validation = KafkaMessagingOptionsValidation.Validate(
            configuration.Kafka,
            configuration.EnvironmentName);
        if (validation.Failed)
        {
            return Rejected(
                "kafka_configuration_invalid",
                string.Join(" ", validation.Failures));
        }

        try
        {
            _ = configuration.Kafka.BuildClientConfig();
        }
        catch (InvalidOperationException)
        {
            return Rejected(
                "kafka_security_invalid",
                "Kafka security configuration is invalid.");
        }

        if (!options.Execute)
        {
            return Allowed();
        }

        if (!configuration.ExecutionEnabled)
        {
            return Rejected(
                "execution_disabled",
                "Kafka capacity execution is disabled by environment configuration.");
        }

        if (string.IsNullOrWhiteSpace(options.ApprovalId))
        {
            return Rejected(
                "approval_required",
                "An approval identifier is required for Kafka capacity execution.");
        }

        if (string.IsNullOrWhiteSpace(options.Reason))
        {
            return Rejected(
                "reason_required",
                "A reason is required for Kafka capacity execution.");
        }

        return Allowed();
    }

    /// <summary>
    /// 在创建临时 Topic 前验证真实集群身份和副本能力。
    /// </summary>
    public static KafkaCapacityGuardResult ValidateCluster(
        KafkaCapacityConfiguration configuration,
        KafkaCapacityOptions options,
        KafkaCapacityClusterIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var planResult = ValidatePlan(configuration, options);
        if (!planResult.IsAllowed)
        {
            return planResult;
        }

        if (!string.Equals(
                configuration.ExpectedClusterId,
                identity.ClusterId,
                StringComparison.Ordinal))
        {
            return Rejected(
                "cluster_id_mismatch",
                "Connected Kafka Cluster Id does not match the approved capacity target.");
        }

        if (identity.BrokerCount < options.ReplicationFactor)
        {
            return Rejected(
                "insufficient_brokers",
                "Kafka broker count is below the requested replication factor.");
        }

        return Allowed();
    }

    private static KafkaCapacityGuardResult Allowed() =>
        new(true, "allowed", "Kafka capacity guard checks passed.");

    private static KafkaCapacityGuardResult Rejected(
        string reasonCode,
        string message) =>
        new(false, reasonCode, message);
}
