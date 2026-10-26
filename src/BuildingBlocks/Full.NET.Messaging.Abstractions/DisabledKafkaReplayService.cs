namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// Native AOT API 宿主上的 Kafka 范围重放占位实现；编译期排除 Confluent 闭包，运行期由门禁保持关闭。
/// </summary>
internal sealed class DisabledKafkaReplayService : IKafkaReplayService, ITrackedKafkaReplayService
{
    /// <inheritdoc/>
    public Task<KafkaReplayResult> ReplayTrackedAsync(KafkaReplayRequest request, KafkaReplaySnapshot? snapshot,
        IKafkaReplayProgressSink progress, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Kafka tracked replay is not available in this API deployment.");

    /// <inheritdoc />
    public Task<KafkaReplayResult> ReplayAsync(
        KafkaReplayRequest request,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(
            "Kafka range replay is not available in the Native AOT API host.");
}
