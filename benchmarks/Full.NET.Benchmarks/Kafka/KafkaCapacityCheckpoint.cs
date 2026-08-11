using System.Text.Json;
using System.Text.Json.Serialization;

namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 保存完整关闭样本及续跑所需的构建、场景和 Topic 身份指纹。
/// </summary>
public sealed record KafkaCapacityCheckpoint
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required string BuildFingerprint { get; init; }

    public required string ScenarioFingerprint { get; init; }

    public required string ScopeCode { get; init; }

    public required string RunId { get; init; }

    public required KafkaCapacityTopicIdentity TopicIdentity { get; init; }

    public IReadOnlyList<KafkaCapacitySampleEvidence> CompletedSamples { get; init; } = [];

    [JsonIgnore]
    public IReadOnlyList<string> CompletedSampleIds =>
        CompletedSamples.Select(static sample => sample.SampleId).ToArray();

    public static KafkaCapacityCheckpoint Create(
        string buildFingerprint,
        string scenarioFingerprint,
        string scopeCode,
        KafkaCapacityTopicIdentity topicIdentity,
        string runId) =>
        new()
        {
            BuildFingerprint = Require(buildFingerprint, nameof(buildFingerprint)),
            ScenarioFingerprint = Require(scenarioFingerprint, nameof(scenarioFingerprint)),
            ScopeCode = Require(scopeCode, nameof(scopeCode)),
            RunId = Require(runId, nameof(runId)),
            TopicIdentity = topicIdentity
                ?? throw new ArgumentNullException(nameof(topicIdentity)),
        };

    public static async Task<KafkaCapacityCheckpoint?> LoadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(fullPath);
        var checkpoint = await JsonSerializer.DeserializeAsync<KafkaCapacityCheckpoint>(
            stream,
            SerializerOptions,
            cancellationToken);
        if (checkpoint is null)
        {
            throw new InvalidDataException("Kafka capacity checkpoint is empty.");
        }

        checkpoint.ValidateShape();
        return checkpoint;
    }

    public static async Task<KafkaCapacityCheckpoint> SaveInitialAsync(
        string path,
        KafkaCapacityCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        checkpoint.ValidateShape();
        return await WriteAsync(path, checkpoint, cancellationToken);
    }

    public static async Task<KafkaCapacityCheckpoint> SaveSampleAsync(
        string path,
        KafkaCapacityCheckpoint checkpoint,
        KafkaCapacitySampleEvidence evidence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(evidence);
        checkpoint.ValidateShape();
        if (!string.Equals(
                checkpoint.ScopeCode,
                evidence.ScopeCode,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Kafka capacity sample scope does not match the checkpoint.");
        }

        var updated = checkpoint;
        if (evidence.State == KafkaCapacitySampleState.Completed)
        {
            if (!evidence.Integrity.CorrectnessPassed
                || evidence.FailureCodes.Count != 0)
            {
                throw new InvalidDataException(
                    "A completed Kafka capacity sample must pass correctness without failures.");
            }

            if (checkpoint.CompletedSamples.Any(sample => string.Equals(
                    sample.SampleId,
                    evidence.SampleId,
                    StringComparison.Ordinal)))
            {
                throw new InvalidDataException(
                    "Kafka capacity checkpoint already contains this sample.");
            }

            updated = checkpoint with
            {
                CompletedSamples = checkpoint.CompletedSamples
                    .Append(evidence)
                    .OrderBy(static sample => sample.SampleId, StringComparer.Ordinal)
                    .ToArray(),
            };
        }

        return await WriteAsync(path, updated, cancellationToken);
    }

    private static async Task<KafkaCapacityCheckpoint> WriteAsync(
        string path,
        KafkaCapacityCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    checkpoint,
                    SerializerOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
            return checkpoint;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public void ValidateResume(
        string buildFingerprint,
        string scenarioFingerprint,
        string scopeCode,
        KafkaCapacityTopicIdentity topicIdentity,
        string runId)
    {
        ValidateShape();
        if (!string.Equals(BuildFingerprint, buildFingerprint, StringComparison.Ordinal)
            || !string.Equals(
                ScenarioFingerprint,
                scenarioFingerprint,
                StringComparison.Ordinal)
            || !string.Equals(ScopeCode, scopeCode, StringComparison.Ordinal)
            || !string.Equals(RunId, runId, StringComparison.Ordinal)
            || TopicIdentity != topicIdentity)
        {
            throw new InvalidDataException(
                "Kafka capacity checkpoint fingerprint does not match this run.");
        }
    }

    private static JsonSerializerOptions SerializerOptions { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private void ValidateShape()
    {
        if (SchemaVersion != CurrentSchemaVersion
            || string.IsNullOrWhiteSpace(BuildFingerprint)
            || string.IsNullOrWhiteSpace(ScenarioFingerprint)
            || string.IsNullOrWhiteSpace(ScopeCode)
            || string.IsNullOrWhiteSpace(RunId)
            || TopicIdentity is null
            || CompletedSamples is null
            || CompletedSamples.Any(sample =>
                sample is null
                || sample.State != KafkaCapacitySampleState.Completed
                || !sample.Integrity.CorrectnessPassed
                || sample.FailureCodes.Count != 0
                || !string.Equals(sample.ScopeCode, ScopeCode, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(sample.SampleId))
            || CompletedSampleIds.Count
            != CompletedSampleIds.Distinct(StringComparer.Ordinal).Count())
        {
            throw new InvalidDataException(
                "Kafka capacity checkpoint schema or required values are invalid.");
        }
    }

    private static string Require(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }
}
