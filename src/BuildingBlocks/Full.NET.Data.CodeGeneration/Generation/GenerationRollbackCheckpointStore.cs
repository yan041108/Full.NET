using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>
/// 在工作区内部原子发布 Apply 前置检查点，且绝不覆盖同一运行标识的既有证据。
/// </summary>
public static class GenerationRollbackCheckpointStore
{
    public const string RootRelativePath =
        ".fullnet/codegeneration-rollback-checkpoints";

    private const int CurrentSchemaVersion = 1;
    private const string MetadataFileName = "checkpoint.json";
    private const string ContentsDirectoryName = "contents";
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    public static async Task CreateAsync(
        string workspaceRoot,
        Guid applyRunId,
        GenerationWritePlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (applyRunId == Guid.Empty)
        {
            throw new ArgumentException(
                "Apply 运行标识不能为空。",
                nameof(applyRunId));
        }

        if (!plan.CanApply || plan.NextManifest is null)
        {
            throw new InvalidOperationException(
                "只有无冲突的写盘计划才能建立回滚检查点。");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var fullRoot = GenerationWorkspacePath.NormalizeRoot(workspaceRoot);
        GenerationWorkspacePath.EnsureParentDirectory(
            fullRoot,
            $"{RootRelativePath}/placeholder");
        var finalRelativePath = $"{RootRelativePath}/{applyRunId:N}";
        var finalPath = GenerationWorkspacePath.Resolve(
            fullRoot,
            finalRelativePath);
        if (Directory.Exists(finalPath) || File.Exists(finalPath))
        {
            throw new GenerationWorkspaceConflictException(
                "同一 Apply 运行的回滚检查点已经存在，禁止覆盖。",
                finalRelativePath);
        }

        var previousContents = await ReadPreviousContentsAsync(
            fullRoot,
            plan.PreviousManifest,
            cancellationToken);
        var document = new CheckpointDocument
        {
            SchemaVersion = CurrentSchemaVersion,
            ApplyRunId = applyRunId,
            AppliedManifest = plan.NextManifest.ToJson(),
            AppliedManifestSha256 = GenerationContentHash.Compute(
                plan.NextManifest.ToJson()),
            PreviousManifest = plan.PreviousManifest?.ToJson(),
            PreviousManifestSha256 = plan.PreviousManifest is null
                ? null
                : GenerationContentHash.Compute(
                    plan.PreviousManifest.ToJson()),
            PreviousArtifacts = previousContents
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => new CheckpointArtifactDocument
                {
                    RelativePath = item.Key,
                    Sha256 = GenerationContentHash.Compute(item.Value),
                })
                .ToArray(),
        };
        var pendingRelativePath =
            $"{RootRelativePath}/pending-{applyRunId:N}-{Guid.NewGuid():N}";
        var pendingPath = GenerationWorkspacePath.Resolve(
            fullRoot,
            pendingRelativePath);
        Directory.CreateDirectory(pendingPath);
        try
        {
            var contentsPath = Path.Combine(
                pendingPath,
                ContentsDirectoryName);
            Directory.CreateDirectory(contentsPath);
            foreach (var content in previousContents.Values.Distinct(
                         StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sha256 = GenerationContentHash.Compute(content);
                await WriteDurableTextAsync(
                    Path.Combine(contentsPath, $"{sha256}.txt"),
                    content,
                    cancellationToken);
            }

            var json = JsonSerializer.Serialize(document, SerializerOptions)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                + "\n";
            await WriteDurableTextAsync(
                Path.Combine(pendingPath, MetadataFileName),
                json,
                cancellationToken);
            Directory.Move(pendingPath, finalPath);
        }
        catch
        {
            TryDeletePendingDirectory(pendingPath);
            throw;
        }
    }

    public static async Task<GenerationRollbackCheckpoint> ReadAsync(
        string workspaceRoot,
        Guid applyRunId,
        CancellationToken cancellationToken = default)
    {
        if (applyRunId == Guid.Empty)
        {
            throw new ArgumentException(
                "Apply 运行标识不能为空。",
                nameof(applyRunId));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var fullRoot = GenerationWorkspacePath.NormalizeRoot(workspaceRoot);
        var checkpointRelativePath =
            $"{RootRelativePath}/{applyRunId:N}";
        var checkpointPath = GenerationWorkspacePath.Resolve(
            fullRoot,
            checkpointRelativePath);
        if (!Directory.Exists(checkpointPath))
        {
            throw new DirectoryNotFoundException(
                "指定 Apply 运行没有回滚检查点。");
        }

        var metadataPath = GenerationWorkspacePath.Resolve(
            fullRoot,
            $"{checkpointRelativePath}/{MetadataFileName}");
        var json = await File.ReadAllTextAsync(
            metadataPath,
            StrictUtf8,
            cancellationToken);
        CheckpointDocument document;
        try
        {
            document = JsonSerializer.Deserialize<CheckpointDocument>(
                    json,
                    SerializerOptions)
                ?? throw new ArgumentException("回滚检查点元数据为空。");
        }
        catch (JsonException exception)
        {
            throw new ArgumentException(
                "回滚检查点元数据不是有效 JSON。",
                nameof(workspaceRoot),
                exception);
        }

        if (document.SchemaVersion != CurrentSchemaVersion
            || document.ApplyRunId != applyRunId
            || string.IsNullOrWhiteSpace(document.AppliedManifest)
            || !GenerationContentHash.IsValid(
                document.AppliedManifestSha256)
            || document.PreviousArtifacts is null)
        {
            throw new ArgumentException("回滚检查点元数据不完整。");
        }

        var appliedManifest = GenerationManifest.Parse(
            document.AppliedManifest);
        if (!StringComparer.Ordinal.Equals(
                GenerationContentHash.Compute(document.AppliedManifest),
                document.AppliedManifestSha256))
        {
            throw new ArgumentException("回滚检查点计划清单已发生变化。");
        }

        var previousManifest = document.PreviousManifest is null
            ? null
            : GenerationManifest.Parse(document.PreviousManifest);
        if (previousManifest is null)
        {
            if (document.PreviousManifestSha256 is not null)
            {
                throw new ArgumentException("回滚检查点旧清单摘要不完整。");
            }
        }
        else if (!GenerationContentHash.IsValid(
                     document.PreviousManifestSha256)
                 || !StringComparer.Ordinal.Equals(
                     GenerationContentHash.Compute(document.PreviousManifest!),
                     document.PreviousManifestSha256))
        {
            throw new ArgumentException("回滚检查点旧清单已发生变化。");
        }
        var expectedEntries = previousManifest?.Artifacts
            ?? Array.Empty<GenerationManifestEntry>();
        if (document.PreviousArtifacts.Length != expectedEntries.Count)
        {
            throw new ArgumentException("回滚检查点旧内容目录与旧清单不一致。");
        }

        var previousContents = new Dictionary<string, string>(
            StringComparer.Ordinal);
        foreach (var entry in expectedEntries)
        {
            var metadata = document.PreviousArtifacts.SingleOrDefault(item =>
                item.RelativePath == entry.RelativePath);
            if (metadata is null
                || !StringComparer.Ordinal.Equals(
                    metadata.Sha256,
                    entry.Sha256))
            {
                throw new ArgumentException("回滚检查点旧内容摘要与旧清单不一致。");
            }

            var contentPath = GenerationWorkspacePath.Resolve(
                fullRoot,
                $"{checkpointRelativePath}/{ContentsDirectoryName}/"
                + $"{entry.Sha256}.txt");
            var content = await File.ReadAllTextAsync(
                contentPath,
                StrictUtf8,
                cancellationToken);
            if (!StringComparer.Ordinal.Equals(
                    GenerationContentHash.Compute(content),
                    entry.Sha256))
            {
                throw new ArgumentException("回滚检查点旧内容已发生变化。");
            }

            previousContents.Add(entry.RelativePath, content);
        }

        return new GenerationRollbackCheckpoint(
            applyRunId,
            appliedManifest,
            previousManifest,
            previousContents);
    }

    /// <summary>
    /// 在调用方已完成资格与安全校验后删除检查点目录；目录不存在时幂等返回 false。
    /// </summary>
    public static Task<bool> TryDeleteAsync(
        string workspaceRoot,
        Guid applyRunId,
        CancellationToken cancellationToken = default)
    {
        if (applyRunId == Guid.Empty)
        {
            throw new ArgumentException(
                "Apply 运行标识不能为空。",
                nameof(applyRunId));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var fullRoot = GenerationWorkspacePath.NormalizeRoot(workspaceRoot);
        var checkpointRelativePath = $"{RootRelativePath}/{applyRunId:N}";
        var checkpointPath = GenerationWorkspacePath.Resolve(
            fullRoot,
            checkpointRelativePath);
        if (!Directory.Exists(checkpointPath))
        {
            return Task.FromResult(false);
        }

        if (!File.Exists(Path.Combine(checkpointPath, MetadataFileName)))
        {
            throw new ArgumentException("回滚检查点元数据不完整。");
        }

        Directory.Delete(checkpointPath, recursive: true);
        return Task.FromResult(true);
    }

    private static async Task<IReadOnlyDictionary<string, string>>
        ReadPreviousContentsAsync(
            string fullRoot,
            GenerationManifest? previousManifest,
            CancellationToken cancellationToken)
    {
        var contents = new Dictionary<string, string>(StringComparer.Ordinal);
        if (previousManifest is null)
        {
            return contents;
        }

        foreach (var entry in previousManifest.Artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = GenerationWorkspacePath.Resolve(
                fullRoot,
                entry.RelativePath);
            if (!File.Exists(path) || Directory.Exists(path))
            {
                throw new GenerationWorkspaceConflictException(
                    "旧清单拥有的产物不存在，无法建立完整回滚检查点。",
                    entry.RelativePath);
            }

            var content = await File.ReadAllTextAsync(
                path,
                StrictUtf8,
                cancellationToken);
            if (!StringComparer.Ordinal.Equals(
                    GenerationContentHash.Compute(content),
                    entry.Sha256))
            {
                throw new GenerationWorkspaceConflictException(
                    "旧清单拥有的产物已变化，无法建立回滚检查点。",
                    entry.RelativePath);
            }

            contents.Add(entry.RelativePath, content);
        }

        return contents;
    }

    private static void TryDeletePendingDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // 原始失败优先向上传播；残留目录仍带 pending 前缀，不会被误判为可回滚证据。
        }
        catch (UnauthorizedAccessException)
        {
            // 无法清理时保留精确任务目录供运维审查，不得覆盖原始异常。
        }
    }

    private static async Task WriteDurableTextAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        var bytes = StrictUtf8.GetBytes(content);
        await using var stream = new FileStream(
            path,
            new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
            });
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    private sealed class CheckpointDocument
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; init; }

        [JsonPropertyName("applyRunId")]
        public Guid ApplyRunId { get; init; }

        [JsonPropertyName("appliedManifest")]
        public string? AppliedManifest { get; init; }

        [JsonPropertyName("appliedManifestSha256")]
        public string? AppliedManifestSha256 { get; init; }

        [JsonPropertyName("previousManifest")]
        public string? PreviousManifest { get; init; }

        [JsonPropertyName("previousManifestSha256")]
        public string? PreviousManifestSha256 { get; init; }

        [JsonPropertyName("previousArtifacts")]
        public CheckpointArtifactDocument[]? PreviousArtifacts { get; init; }
    }

    private sealed class CheckpointArtifactDocument
    {
        [JsonPropertyName("relativePath")]
        public string? RelativePath { get; init; }

        [JsonPropertyName("sha256")]
        public string? Sha256 { get; init; }
    }
}
