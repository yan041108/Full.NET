namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 按稳定顺序执行样本，并在每个完整样本后持久化 checkpoint。
/// </summary>
public sealed class KafkaCapacityRunner(
    IKafkaCapacityScenarioDriver driver,
    IKafkaCapacityCheckpointStore checkpointStore)
{
    public async Task<IReadOnlyList<KafkaCapacitySampleEvidence>> ExecuteSamplesAsync(
        IReadOnlyList<KafkaCapacitySampleContext> contexts,
        string checkpointPath,
        KafkaCapacityCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contexts);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointPath);
        ArgumentNullException.ThrowIfNull(checkpoint);
        var evidence = new List<KafkaCapacitySampleEvidence>(contexts.Count);
        var currentCheckpoint = checkpoint;
        foreach (var context in contexts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (currentCheckpoint.CompletedSampleIds.Contains(
                    context.Sample.SampleId,
                    StringComparer.Ordinal))
            {
                continue;
            }

            var sample = await driver.ExecuteAsync(context, cancellationToken);
            evidence.Add(sample);
            currentCheckpoint = await checkpointStore.SaveAsync(
                checkpointPath,
                currentCheckpoint,
                sample,
                cancellationToken);
            if (sample.State == KafkaCapacitySampleState.Incomplete
                || !sample.Integrity.CorrectnessPassed)
            {
                break;
            }
        }

        return evidence;
    }
}

/// <summary>
/// 使用原子 JSON checkpoint 实现 Runner 的样本提交边界。
/// </summary>
public sealed class FileKafkaCapacityCheckpointStore : IKafkaCapacityCheckpointStore
{
    public async Task<KafkaCapacityCheckpoint> SaveAsync(
        string path,
        KafkaCapacityCheckpoint checkpoint,
        KafkaCapacitySampleEvidence evidence,
        CancellationToken cancellationToken) =>
        await KafkaCapacityCheckpoint.SaveCompletedAsync(
            path,
            checkpoint,
            evidence.SampleId,
            evidence.State == KafkaCapacitySampleState.Completed,
            evidence.ScopeCode,
            cancellationToken);
}
