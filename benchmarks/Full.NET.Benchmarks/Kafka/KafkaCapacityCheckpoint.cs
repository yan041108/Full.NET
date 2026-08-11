using System.Text.Json;
using System.Text.Json.Serialization;

namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 保存完整关闭样本及续跑所需的构建、场景和 Topic 身份指纹。
/// </summary>
public sealed record KafkaCapacityCheckpoint
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required string BuildFingerprint { get; init; }

    public required string ScenarioFingerprint { get; init; }

    public required KafkaCapacityTopicIdentity TopicIdentity { get; init; }

    public IReadOnlyList<string> CompletedSampleIds { get; init; } = [];

    public static KafkaCapacityCheckpoint Create(
        string buildFingerprint,
        string scenarioFingerprint,
        KafkaCapacityTopicIdentity topicIdentity) =>
        new()
        {
            BuildFingerprint = Require(buildFingerprint, nameof(buildFingerprint)),
            ScenarioFingerprint = Require(scenarioFingerprint, nameof(scenarioFingerprint)),
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

    public static async Task<KafkaCapacityCheckpoint> SaveCompletedAsync(
        string path,
        KafkaCapacityCheckpoint checkpoint,
        string sampleId,
        bool sampleCompleted,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        checkpoint.ValidateShape();
        Require(sampleId, nameof(sampleId));
        var completed = sampleCompleted
            ? checkpoint.CompletedSampleIds
                .Append(sampleId)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray()
            : checkpoint.CompletedSampleIds;
        var updated = checkpoint with { CompletedSampleIds = completed };
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
                    updated,
                    SerializerOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
            return updated;
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
        KafkaCapacityTopicIdentity topicIdentity)
    {
        ValidateShape();
        if (!string.Equals(BuildFingerprint, buildFingerprint, StringComparison.Ordinal)
            || !string.Equals(
                ScenarioFingerprint,
                scenarioFingerprint,
                StringComparison.Ordinal)
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
            || TopicIdentity is null
            || CompletedSampleIds.Any(string.IsNullOrWhiteSpace)
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
