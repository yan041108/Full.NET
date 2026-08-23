namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>
/// 根据当前文本快照和上一版清单生成不覆盖用户修改的写盘计划。
/// </summary>
public static class GenerationWritePlanner
{
    /// <summary>
    /// 根据期望产物与当前磁盘快照规划写盘动作；用户已修改且不在上一版清单拥有的产物会被标记为 Conflict。
    /// </summary>
    /// <param name="artifacts">本次生成期望写出的产物集合。</param>
    /// <param name="existingFiles">按相对路径索引的当前磁盘文本快照。</param>
    /// <param name="previousManifest">上一版已提交清单；为空表示工作区尚未受管。</param>
    /// <returns>包含逐条动作与下一版清单的写盘计划；存在冲突时 NextManifest 为空。</returns>
    public static GenerationWritePlan Plan(
        IReadOnlyList<GeneratedArtifact> artifacts,
        IReadOnlyDictionary<string, string> existingFiles,
        GenerationManifest? previousManifest = null)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        var orderedArtifacts = ValidateAndOrderArtifacts(artifacts);
        var desiredContents = orderedArtifacts.ToDictionary(
            artifact => artifact.RelativePath,
            artifact => artifact.Content,
            StringComparer.Ordinal);
        return PlanFromDesiredContents(
            desiredContents,
            existingFiles,
            previousManifest);
    }

    /// <summary>
    /// 按路径与内容对规划写盘动作；供正向生成与逆向回滚共享，避免伪造 GeneratedArtifactKind。
    /// </summary>
    internal static GenerationWritePlan PlanFromDesiredContents(
        IReadOnlyDictionary<string, string> desiredContents,
        IReadOnlyDictionary<string, string> existingFiles,
        GenerationManifest? previousManifest = null)
    {
        ArgumentNullException.ThrowIfNull(desiredContents);
        ArgumentNullException.ThrowIfNull(existingFiles);

        var orderedDesired = ValidateAndOrderDesiredContents(desiredContents);
        var currentFiles = ValidateCurrentFiles(existingFiles);
        var desiredManifestEntries = new List<GenerationManifestEntry>(
            orderedDesired.Count);
        var desiredPaths = new HashSet<string>(StringComparer.Ordinal);
        var desiredPortablePaths = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var actions = new List<GenerationWriteAction>(
            orderedDesired.Count
            + (previousManifest?.Artifacts.Count ?? 0));
        var hasConflict = false;

        foreach (var desired in orderedDesired)
        {
            desiredPaths.Add(desired.RelativePath);
            desiredPortablePaths.Add(desired.RelativePath);
            var desiredSha256 = GenerationContentHash.Compute(desired.Content);
            desiredManifestEntries.Add(
                new GenerationManifestEntry(
                    desired.RelativePath,
                    desiredSha256));

            currentFiles.TryGetValue(
                desired.RelativePath,
                out var existingFile);
            var existingSha256 = existingFile is null
                ? null
                : GenerationContentHash.Compute(existingFile.Content);
            var kind = Classify(
                desired.RelativePath,
                desired.Content,
                desiredSha256,
                existingFile,
                existingSha256,
                previousManifest);
            hasConflict |= kind == GenerationWriteActionKind.Conflict;
            actions.Add(new GenerationWriteAction(
                desired.RelativePath,
                kind,
                desired.Content,
                existingSha256,
                desiredSha256));
        }

        if (previousManifest is not null)
        {
            foreach (var previousEntry in previousManifest.Artifacts)
            {
                if (desiredPaths.Contains(previousEntry.RelativePath))
                {
                    continue;
                }

                currentFiles.TryGetValue(
                    previousEntry.RelativePath,
                    out var existingFile);
                var existingSha256 = existingFile is null
                    ? null
                    : GenerationContentHash.Compute(existingFile.Content);

                if (desiredPortablePaths.Contains(previousEntry.RelativePath)
                    || (existingFile is not null
                        && !StringComparer.Ordinal.Equals(
                            existingFile.RelativePath,
                            previousEntry.RelativePath)))
                {
                    hasConflict = true;
                    actions.Add(new GenerationWriteAction(
                        previousEntry.RelativePath,
                        GenerationWriteActionKind.Conflict,
                        Content: null,
                        existingSha256,
                        DesiredSha256: null));
                    continue;
                }

                if (existingFile is null || existingSha256 is null)
                {
                    continue;
                }

                var kind = StringComparer.Ordinal.Equals(
                        existingSha256,
                        previousEntry.Sha256)
                    ? GenerationWriteActionKind.Delete
                    : GenerationWriteActionKind.Conflict;
                hasConflict |= kind == GenerationWriteActionKind.Conflict;
                actions.Add(new GenerationWriteAction(
                    previousEntry.RelativePath,
                    kind,
                    Content: null,
                    existingSha256,
                    DesiredSha256: null));
            }
        }

        actions.Sort((left, right) => StringComparer.Ordinal.Compare(
            left.RelativePath,
            right.RelativePath));
        var nextManifest = hasConflict
            ? null
            : GenerationManifest.Create(desiredManifestEntries);
        return new GenerationWritePlan(
            actions,
            previousManifest,
            nextManifest);
    }

    private static GenerationWriteActionKind Classify(
        string relativePath,
        string desiredContent,
        string desiredSha256,
        CurrentFile? existingFile,
        string? existingSha256,
        GenerationManifest? previousManifest)
    {
        if (existingFile is null || existingSha256 is null)
        {
            return GenerationWriteActionKind.Create;
        }

        if (!StringComparer.Ordinal.Equals(
                existingFile.RelativePath,
                relativePath))
        {
            return GenerationWriteActionKind.Conflict;
        }

        if (StringComparer.Ordinal.Equals(
                existingFile.Content,
                desiredContent))
        {
            return GenerationWriteActionKind.Unchanged;
        }

        if (previousManifest is not null
            && previousManifest.TryGetSha256(
                relativePath,
                out var previousSha256)
            && StringComparer.Ordinal.Equals(
                existingSha256,
                previousSha256))
        {
            return GenerationWriteActionKind.Update;
        }

        return GenerationWriteActionKind.Conflict;
    }

    private static IReadOnlyList<GeneratedArtifact> ValidateAndOrderArtifacts(
        IReadOnlyList<GeneratedArtifact> artifacts)
    {
        var ordered = artifacts
            .Select(artifact =>
            {
                ArgumentNullException.ThrowIfNull(artifact);
                GenerationArtifactPath.Validate(
                    artifact.RelativePath,
                    nameof(artifact.RelativePath));
                ArgumentNullException.ThrowIfNull(artifact.Content);
                return artifact;
            })
            .OrderBy(
                artifact => artifact.RelativePath,
                StringComparer.Ordinal)
            .ToArray();

        var portablePaths = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var artifact in ordered)
        {
            if (!portablePaths.Add(artifact.RelativePath))
            {
                throw new ArgumentException(
                    $"生成产物包含重复或不可移植的路径别名：{artifact.RelativePath}",
                    nameof(artifacts));
            }
        }

        return ordered;
    }

    private static IReadOnlyList<DesiredContent> ValidateAndOrderDesiredContents(
        IReadOnlyDictionary<string, string> desiredContents)
    {
        var ordered = desiredContents
            .Select(pair =>
            {
                var relativePath = GenerationArtifactPath.Validate(
                    pair.Key,
                    nameof(desiredContents));
                ArgumentNullException.ThrowIfNull(pair.Value);
                return new DesiredContent(relativePath, pair.Value);
            })
            .OrderBy(
                item => item.RelativePath,
                StringComparer.Ordinal)
            .ToArray();

        var portablePaths = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var item in ordered)
        {
            if (!portablePaths.Add(item.RelativePath))
            {
                throw new ArgumentException(
                    $"期望内容包含重复或不可移植的路径别名：{item.RelativePath}",
                    nameof(desiredContents));
            }
        }

        return ordered;
    }

    private static IReadOnlyDictionary<string, CurrentFile> ValidateCurrentFiles(
        IReadOnlyDictionary<string, string> existingFiles)
    {
        var validated = new Dictionary<string, CurrentFile>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var pair in existingFiles)
        {
            var relativePath = GenerationArtifactPath.Validate(
                pair.Key,
                nameof(existingFiles));
            ArgumentNullException.ThrowIfNull(pair.Value);
            if (!validated.TryAdd(
                    relativePath,
                    new CurrentFile(relativePath, pair.Value)))
            {
                throw new ArgumentException(
                    $"当前文件快照包含重复或不可移植的路径别名：{relativePath}",
                    nameof(existingFiles));
            }
        }

        return validated;
    }

    private sealed record CurrentFile(
        string RelativePath,
        string Content);

    private sealed record DesiredContent(
        string RelativePath,
        string Content);
}