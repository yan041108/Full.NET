using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Confluent.Kafka;
using Full.NET.Messaging.Kafka;

namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 按稳定顺序执行样本，并在每个完整样本后持久化 checkpoint。
/// </summary>
public sealed class KafkaCapacityRunner(
    IKafkaCapacityScenarioDriver driver,
    IKafkaCapacityCheckpointStore checkpointStore)
{
    private static readonly TimeSpan EvidencePersistenceTimeout =
        TimeSpan.FromSeconds(5);

    public static async Task<KafkaCapacityExitCode> RunCommandAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var options = KafkaCapacityOptions.Parse(arguments);
            var configuration = KafkaCapacityConfiguration.Load(options);
            var samples = KafkaCapacityScenarioCatalog.Build(options);
            var planGuard = KafkaCapacityEnvironmentGuard.ValidatePlan(
                configuration,
                options);
            if (!planGuard.IsAllowed)
            {
                Console.Error.WriteLine(planGuard.ReasonCode);
                return KafkaCapacityExitCode.EnvironmentRejected;
            }

            if (!options.Execute)
            {
                Console.WriteLine(configuration.ToString());
                foreach (var sample in samples)
                {
                    Console.WriteLine(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"{sample.ScopeCode} {sample.SampleId} rate={sample.TargetMessagesPerSecond} payload={sample.PayloadSizeBytes} concurrency={sample.ProducerConcurrency}"));
                }

                Console.WriteLine("CapacityStatus=Capacity-not-verified; Mode=DryRun");
                return KafkaCapacityExitCode.Success;
            }

            return await ExecuteCommandAsync(
                options,
                configuration,
                samples,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return KafkaCapacityExitCode.Cancelled;
        }
        catch (KafkaCapacityControlPlaneException exception)
        {
            Console.Error.WriteLine(exception.ReasonCode);
            return KafkaCapacityExitCode.EnvironmentRejected;
        }
        catch (ArgumentException)
        {
            return KafkaCapacityExitCode.InvalidConfiguration;
        }
        catch (InvalidDataException)
        {
            return KafkaCapacityExitCode.InvalidConfiguration;
        }
        catch (JsonException)
        {
            return KafkaCapacityExitCode.InvalidConfiguration;
        }
        catch (KafkaException)
        {
            return KafkaCapacityExitCode.DependencyOrIncomplete;
        }
        catch (IOException)
        {
            return KafkaCapacityExitCode.DependencyOrIncomplete;
        }
        catch (TimeoutException)
        {
            return KafkaCapacityExitCode.DependencyOrIncomplete;
        }
    }

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
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (currentCheckpoint.CompletedSampleIds.Contains(
                    context.Sample.SampleId,
                    StringComparer.Ordinal))
            {
                continue;
            }

            var sample = await driver.ExecuteAsync(context, cancellationToken);
            evidence.Add(sample);
            using var persistenceCancellation = new CancellationTokenSource(
                EvidencePersistenceTimeout);
            currentCheckpoint = await checkpointStore.SaveAsync(
                checkpointPath,
                currentCheckpoint,
                sample,
                persistenceCancellation.Token);
            if (sample.State == KafkaCapacitySampleState.Incomplete
                || !sample.Integrity.CorrectnessPassed)
            {
                break;
            }
        }

        return evidence;
    }

    private static async Task<KafkaCapacityExitCode> ExecuteCommandAsync(
        KafkaCapacityOptions options,
        KafkaCapacityConfiguration configuration,
        IReadOnlyList<KafkaCapacitySample> samples,
        CancellationToken cancellationToken)
    {
        var generatedRunId = string.Create(
            CultureInfo.InvariantCulture,
            $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}");
        var buildFingerprint = await ResolveBuildFingerprintAsync(cancellationToken);
        var scenarioFingerprint = BuildScenarioFingerprint(
            options,
            samples,
            configuration.Kafka);
        Directory.CreateDirectory(options.OutputDirectory);
        var checkpointPath = Path.Combine(
            options.OutputDirectory,
            "checkpoint.json");
        var existingCheckpoint = await KafkaCapacityCheckpoint.LoadAsync(
            checkpointPath,
            cancellationToken);
        if (existingCheckpoint is not null && !options.Resume)
        {
            throw new InvalidDataException(
                "Existing checkpoint requires --resume true.");
        }

        var runId = options.RunId
            ?? existingCheckpoint?.RunId
            ?? generatedRunId[..24];

        using var admin = new AdminClientBuilder(
                configuration.Kafka.BuildClientConfig())
            .Build();
        var adminAdapter = new ConfluentKafkaCapacityAdminClient(
            admin,
            TimeSpan.FromMilliseconds(
                configuration.Kafka.DeliveryTimeoutMilliseconds));
        var cluster = await adminAdapter.DescribeClusterAsync(cancellationToken);
        var clusterGuard = KafkaCapacityEnvironmentGuard.ValidateCluster(
            configuration,
            options,
            new KafkaCapacityClusterIdentity(
                cluster.ClusterId,
                cluster.BrokerCount));
        if (!clusterGuard.IsAllowed)
        {
            Console.Error.WriteLine(clusterGuard.ReasonCode);
            return KafkaCapacityExitCode.EnvironmentRejected;
        }

        var clusterIdHash = KafkaCapacityFingerprint.Sha256(cluster.ClusterId);
        var topicManager = new KafkaCapacityTopicManager(adminAdapter);
        var topic = await topicManager.EnsureTopicAsync(
            runId,
            clusterIdHash,
            options.Partitions,
            options.ReplicationFactor,
            existingCheckpoint?.TopicIdentity,
            cancellationToken);
        var checkpoint = existingCheckpoint
            ?? KafkaCapacityCheckpoint.Create(
                buildFingerprint,
                scenarioFingerprint,
                KafkaCapacityScopeCodes.KafkaTransport,
                topic,
                runId);
        if (existingCheckpoint is not null)
        {
            checkpoint.ValidateResume(
                buildFingerprint,
                scenarioFingerprint,
                KafkaCapacityScopeCodes.KafkaTransport,
                topic,
                runId);
        }
        else
        {
            checkpoint = await KafkaCapacityCheckpoint.SaveInitialAsync(
                checkpointPath,
                checkpoint,
                cancellationToken: cancellationToken);
        }

        var pendingSamples = samples.Where(sample =>
                !checkpoint.CompletedSampleIds.Contains(
                    sample.SampleId,
                    StringComparer.Ordinal))
            .ToArray();
        if (options.MaximumNewSamples > 0)
        {
            pendingSamples = pendingSamples
                .Take(options.MaximumNewSamples)
                .ToArray();
        }

        var contexts = pendingSamples.Select(sample =>
                KafkaCapacitySampleContext.Create(
                    sample,
                    topic,
                    runId,
                    options.Warmup,
                    options.Duration,
                    options.DrainTimeout,
                    options.MaximumMessagesPerSample))
            .ToArray();
        var transportExecutor = new KafkaCapacityTransportExecutor(
            configuration.Kafka,
            new ConfluentKafkaCapacityProducerFactory(),
            new ConfluentKafkaCapacityConsumerFactory());
        var runner = new KafkaCapacityRunner(
            new KafkaTransportScenarioDriver(transportExecutor),
            new FileKafkaCapacityCheckpointStore());
        var currentEvidence = (await runner.ExecuteSamplesAsync(
                contexts,
                checkpointPath,
                checkpoint,
                cancellationToken))
            .ToArray();
        var persistedCheckpoint = await KafkaCapacityCheckpoint.LoadAsync(
                checkpointPath,
                CancellationToken.None)
            ?? throw new InvalidDataException(
                "Kafka capacity checkpoint disappeared before report projection.");
        var currentIncomplete = currentEvidence
            .Where(static sample => sample.State == KafkaCapacitySampleState.Incomplete)
            .ToDictionary(static sample => sample.SampleId, StringComparer.Ordinal);
        var completed = persistedCheckpoint.CompletedSamples
            .ToDictionary(static sample => sample.SampleId, StringComparer.Ordinal);
        var evidence = samples
            .Where(sample => completed.ContainsKey(sample.SampleId)
                || currentIncomplete.ContainsKey(sample.SampleId))
            .Select(sample => currentIncomplete.GetValueOrDefault(sample.SampleId)
                ?? completed[sample.SampleId])
            .ToArray();

        var budgetProvided = !string.IsNullOrWhiteSpace(options.BudgetPath);
        var runCancelled = cancellationToken.IsCancellationRequested
            || evidence.Any(static sample =>
                sample.FailureCodes.Contains("cancelled", StringComparer.Ordinal));
        if (budgetProvided && !runCancelled)
        {
            var budget = await KafkaCapacityBudget.LoadAsync(
                options.BudgetPath!,
                cancellationToken);
            for (var index = 0; index < evidence.Length; index++)
            {
                var assessment = budget.Assess(
                    configuration.EnvironmentName,
                    clusterIdHash,
                    buildFingerprint,
                    evidence[index]);
                evidence[index] = evidence[index] with
                {
                    PerformanceBudgetPassed = assessment.Passed,
                    FailureCodes = evidence[index].FailureCodes
                        .Concat(assessment.FailureCodes)
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .ToArray(),
                };
            }
        }

        var manifest = KafkaCapacityReportProjection.CreateManifest(
            configuration.EnvironmentName,
            buildFingerprint,
            runId,
            options.ApprovalId!,
            configuration.Kafka,
            topic);
        await KafkaCapacityReportWriter.WriteAsync(
            options.OutputDirectory,
            new KafkaCapacityReportEvidence(
                manifest,
                evidence,
                transportExecutor.SnapshotStatistics()),
            CancellationToken.None);
        await topicManager.DeleteOwnedTopicAsync(
            topic,
            ShouldDeleteTopic(
                options.DeleteTopic,
                samples,
                evidence,
                runCancelled),
            CancellationToken.None);

        if (runCancelled)
        {
            return KafkaCapacityExitCode.Cancelled;
        }

        return KafkaCapacityExitCodeResolver.Resolve(evidence, budgetProvided);
    }

    internal static bool ShouldDeleteTopic(
        bool deleteRequested,
        IReadOnlyList<KafkaCapacitySample> plannedSamples,
        IReadOnlyList<KafkaCapacitySampleEvidence> evidence,
        bool runCancelled)
    {
        if (!deleteRequested || runCancelled
            || evidence.Count != plannedSamples.Count
            || evidence.Any(static sample =>
                sample.State != KafkaCapacitySampleState.Completed
                || !sample.Integrity.CorrectnessPassed))
        {
            return false;
        }

        var completedIds = evidence
            .Select(static sample => sample.SampleId)
            .ToHashSet(StringComparer.Ordinal);
        return completedIds.SetEquals(
            plannedSamples.Select(static sample => sample.SampleId));
    }

    private static string BuildScenarioFingerprint(
        KafkaCapacityOptions options,
        IReadOnlyList<KafkaCapacitySample> samples,
        KafkaMessagingOptions kafka)
    {
        var value = string.Join(
            ';',
            samples.Select(sample => string.Create(
                CultureInfo.InvariantCulture,
                $"{sample.ScopeCode}|{sample.SampleId}|{sample.Scenario}|{sample.TargetMessagesPerSecond}|{sample.PayloadSizeBytes}|{sample.ProducerConcurrency}|{sample.Repetition}")));
        value += string.Create(
            CultureInfo.InvariantCulture,
            $"|partitions={options.Partitions}|rf={options.ReplicationFactor}|warmup={options.Warmup.TotalSeconds}|duration={options.Duration.TotalSeconds}|drain={options.DrainTimeout.TotalSeconds}|maximum={options.MaximumMessagesPerSample}");
        value += string.Create(
            CultureInfo.InvariantCulture,
            $"|bootstrap={KafkaCapacityFingerprint.Sha256(kafka.BootstrapServers ?? string.Empty)}|client={KafkaCapacityFingerprint.Sha256(kafka.ClientId ?? string.Empty)}|instance={KafkaCapacityFingerprint.Sha256(kafka.ConsumerInstanceId ?? string.Empty)}|security={kafka.SecurityProtocol}|sasl={kafka.SaslMechanism}|messageMax={kafka.MessageMaxBytes}|deliveryTimeout={kafka.DeliveryTimeoutMilliseconds}|consumerProtocol={kafka.ConsumerGroupProtocol}|assignment={kafka.ClassicPartitionAssignment}|consumerQueue={kafka.ConsumerQueueMaxMessagesKbytes}|linger={kafka.ProducerLingerMilliseconds}|batch={kafka.ProducerBatchSizeBytes}|producerQueueMessages={kafka.ProducerQueueMaxMessages}|producerQueueKbytes={kafka.ProducerQueueMaxKbytes}|maxInFlight={kafka.ProducerMaxInFlightRequests}");
        return KafkaCapacityFingerprint.Sha256(value);
    }

    private static async Task<string> ResolveBuildFingerprintAsync(
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "rev-parse HEAD",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        try
        {
            if (process.Start())
            {
                var output = await process.StandardOutput.ReadToEndAsync(
                    cancellationToken);
                await process.WaitForExitAsync(cancellationToken);
                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    return output.Trim();
                }
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // 发布镜像可以不包含 Git；此时使用构建模块指纹并保持 resume 严格匹配。
        }

        return Assembly.GetExecutingAssembly().ManifestModule.ModuleVersionId
            .ToString("N", CultureInfo.InvariantCulture);
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
        await KafkaCapacityCheckpoint.SaveSampleAsync(
            path,
            checkpoint,
            evidence,
            cancellationToken);
}
