using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
using System.Text;

namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>
/// 在真实工作区中捕获并应用生成计划，所有权清单始终最后提交。
/// </summary>
public static class GenerationWorkspaceStore
{
    public const string ManifestRelativePath =
        ".fullnet/codegeneration-manifest.json";

    internal const string LockRelativePath =
        ".fullnet/codegeneration.lock";

    private const string InternalDirectoryRelativePath = ".fullnet";
    private const string DeleteRecoveryDirectoryRelativePath =
        ".fullnet/codegeneration-delete-recovery";
    private const string DeleteRecoveryContentSuffix = ".recovery";
    private const string DeleteRecoveryMetadataSuffix = ".path";
    private const string DeleteRecoveryCommittedSuffix = ".committed";
    private const string ManifestRecoveryFilePrefix =
        "codegeneration-manifest-";
    private const string ManifestRecoveryFileSuffix = ".recovery";

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static async Task<GenerationWorkspaceSnapshot> CaptureAsync(
        string workspaceRoot,
        IReadOnlyList<GeneratedArtifact> artifacts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        cancellationToken.ThrowIfCancellationRequested();

        var fullRoot = GenerationWorkspacePath.NormalizeRoot(workspaceRoot);
        RejectPendingManifestRecovery(fullRoot);
        RejectPendingDeleteRecovery(fullRoot);
        var orderedArtifacts = ValidateArtifacts(artifacts);
        var previousManifest = await ReadManifestAsync(
            fullRoot,
            cancellationToken);
        var pathsToCapture = new HashSet<string>(StringComparer.Ordinal);
        foreach (var artifact in orderedArtifacts)
        {
            pathsToCapture.Add(artifact.RelativePath);
        }

        if (previousManifest is not null)
        {
            foreach (var entry in previousManifest.Artifacts)
            {
                RejectInternalPath(entry.RelativePath);
                pathsToCapture.Add(entry.RelativePath);
            }
        }

        return await CaptureExistingFilesAsync(
            fullRoot,
            pathsToCapture,
            previousManifest,
            cancellationToken);
    }

    /// <summary>
    /// 按中立路径集合捕获工作区快照；供逆向回滚复用，避免伪造 GeneratedArtifactKind。
    /// </summary>
    internal static async Task<GenerationWorkspaceSnapshot> CapturePathsAsync(
        string workspaceRoot,
        IEnumerable<string> relativePaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(relativePaths);
        cancellationToken.ThrowIfCancellationRequested();

        var fullRoot = GenerationWorkspacePath.NormalizeRoot(workspaceRoot);
        RejectPendingManifestRecovery(fullRoot);
        RejectPendingDeleteRecovery(fullRoot);
        var previousManifest = await ReadManifestAsync(
            fullRoot,
            cancellationToken);
        var pathsToCapture = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relativePath in relativePaths)
        {
            var validated = GenerationArtifactPath.Validate(
                relativePath,
                nameof(relativePaths));
            RejectInternalPath(validated);
            pathsToCapture.Add(validated);
        }

        return await CaptureExistingFilesAsync(
            fullRoot,
            pathsToCapture,
            previousManifest,
            cancellationToken);
    }

    private static async Task<GenerationWorkspaceSnapshot>
        CaptureExistingFilesAsync(
            string fullRoot,
            IReadOnlyCollection<string> pathsToCapture,
            GenerationManifest? previousManifest,
            CancellationToken cancellationToken)
    {
        var existingFiles = new Dictionary<string, string>(
            StringComparer.Ordinal);
        foreach (var relativePath in pathsToCapture.OrderBy(
                     path => path,
                     StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = GenerationWorkspacePath.Resolve(
                fullRoot,
                relativePath);
            if (Directory.Exists(fullPath))
            {
                throw Conflict(
                    relativePath,
                    "生成产物路径已被目录占用。");
            }

            if (File.Exists(fullPath))
            {
                existingFiles.Add(
                    relativePath,
                    await ReadStrictTextAsync(
                        fullPath,
                        cancellationToken));
            }
        }

        return new GenerationWorkspaceSnapshot(
            new ReadOnlyDictionary<string, string>(existingFiles),
            previousManifest);
    }

    public static Task ApplyAsync(
        string workspaceRoot,
        GenerationWritePlan plan,
        CancellationToken cancellationToken = default)
    {
        return ApplyCoreAsync(
            workspaceRoot,
            plan,
            beforeManifestCommit: null,
            afterManifestClaim: null,
            afterArtifactCommit: null,
            beforeDeleteClaim: null,
            beforeFirstArtifactCommit: null,
            beforeManifestRecoveryCleanup: null,
            cancellationToken);
    }

    internal static Task ApplyForTestingAsync(
        string workspaceRoot,
        GenerationWritePlan plan,
        Func<Task> beforeManifestCommit,
        Func<Task>? afterManifestClaim = null,
        CancellationToken cancellationToken = default,
        Func<int, Task>? afterArtifactCommit = null,
        Func<Task>? beforeDeleteClaim = null,
        Func<Task>? beforeFirstArtifactCommit = null,
        Func<Task>? beforeManifestRecoveryCleanup = null)
    {
        ArgumentNullException.ThrowIfNull(beforeManifestCommit);
        return ApplyCoreAsync(
            workspaceRoot,
            plan,
            beforeManifestCommit,
            afterManifestClaim,
            afterArtifactCommit,
            beforeDeleteClaim,
            beforeFirstArtifactCommit,
            beforeManifestRecoveryCleanup,
            cancellationToken);
    }

    private static async Task ApplyCoreAsync(
        string workspaceRoot,
        GenerationWritePlan plan,
        Func<Task>? beforeManifestCommit,
        Func<Task>? afterManifestClaim,
        Func<int, Task>? afterArtifactCommit,
        Func<Task>? beforeDeleteClaim,
        Func<Task>? beforeFirstArtifactCommit,
        Func<Task>? beforeManifestRecoveryCleanup,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.CanApply || plan.NextManifest is null)
        {
            throw new InvalidOperationException(
                "包含冲突的生成计划不得写入工作区。");
        }

        cancellationToken.ThrowIfCancellationRequested();
        ValidateActionPaths(plan.Actions);
        var fullRoot = GenerationWorkspacePath.NormalizeRoot(workspaceRoot);
        GenerationWorkspacePath.EnsureParentDirectory(
            fullRoot,
            LockRelativePath);
        await using var workspaceLock = OpenWorkspaceLock(fullRoot);
        RejectPendingManifestRecovery(fullRoot);
        RejectPendingDeleteRecovery(fullRoot);

        await ValidatePlanStateAsync(
            fullRoot,
            plan,
            cancellationToken);

        var stagedFiles = new List<StagedFile>();
        var claimedDeletions = new List<ClaimedDeletion>();
        var manifestCommitted = false;
        Exception? operationException = null;
        try
        {
            foreach (var action in plan.Actions)
            {
                if (action.Kind is not (
                    GenerationWriteActionKind.Create
                    or GenerationWriteActionKind.Update))
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                GenerationWorkspacePath.EnsureParentDirectory(
                    fullRoot,
                    action.RelativePath);
                var targetPath = GenerationWorkspacePath.Resolve(
                    fullRoot,
                    action.RelativePath);
                stagedFiles.Add(new StagedFile(
                    action,
                    targetPath,
                    await StageTextAsync(
                        targetPath,
                        action.Content!,
                        cancellationToken),
                    IsManifest: false));
            }

            GenerationWorkspacePath.EnsureParentDirectory(
                fullRoot,
                ManifestRelativePath);
            var manifestTargetPath = GenerationWorkspacePath.Resolve(
                fullRoot,
                ManifestRelativePath);
            stagedFiles.Add(new StagedFile(
                Action: null,
                manifestTargetPath,
                await StageTextAsync(
                    manifestTargetPath,
                    plan.NextManifest.ToJson(),
                    cancellationToken),
                IsManifest: true));

            await ValidatePlanStateAsync(
                fullRoot,
                plan,
                cancellationToken);

            // 最后一次可取消点必须位于首个不可逆产物提交之前。
            // 进入提交阶段后必须完成清单提交或按冲突恢复语义退出。
            cancellationToken.ThrowIfCancellationRequested();
            var committedArtifactCount = 0;
            foreach (var action in plan.Actions)
            {
                if (action.Kind == GenerationWriteActionKind.Unchanged)
                {
                    continue;
                }

                var commitToken = committedArtifactCount == 0
                    ? cancellationToken
                    : CancellationToken.None;
                await ValidateActionStateAsync(
                    fullRoot,
                    action,
                    commitToken);
                if (committedArtifactCount == 0)
                {
                    if (beforeFirstArtifactCommit is not null)
                    {
                        await beforeFirstArtifactCommit();
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                }

                if (action.Kind == GenerationWriteActionKind.Delete)
                {
                    claimedDeletions.Add(await ClaimDeleteAsync(
                        fullRoot,
                        action,
                        beforeDeleteClaim,
                        commitToken));
                }
                else
                {
                    var stagedFile = stagedFiles.Single(candidate =>
                        !candidate.IsManifest
                        && ReferenceEquals(candidate.Action, action));
                    CommitArtifact(stagedFile);
                }

                committedArtifactCount++;
                if (afterArtifactCommit is not null)
                {
                    await afterArtifactCommit(committedArtifactCount);
                }
            }

            ValidateClaimedDeletionsBeforeManifest(claimedDeletions);
            await ValidateDesiredStateAsync(
                fullRoot,
                plan.Actions,
                committedArtifactCount == 0
                    ? cancellationToken
                    : CancellationToken.None);
            await ValidateManifestStateAsync(
                fullRoot,
                plan.PreviousManifest,
                committedArtifactCount == 0
                    ? cancellationToken
                    : CancellationToken.None);
            if (beforeManifestCommit is not null)
            {
                await beforeManifestCommit();
            }

            if (committedArtifactCount == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var stagedManifest = stagedFiles.Single(
                stagedFile => stagedFile.IsManifest);
            await CommitManifestAsync(
                stagedManifest,
                plan.PreviousManifest,
                afterManifestClaim,
                beforeManifestRecoveryCleanup,
                () => manifestCommitted = true);
            CommitDeletionTombstones(claimedDeletions);
        }
        catch (Exception exception)
        {
            operationException = exception;
            if (!manifestCommitted)
            {
                var restoreException = RestoreClaimedDeletions(
                    claimedDeletions);
                if (restoreException is not null)
                {
                    throw new GenerationWorkspaceConflictException(
                        "生成提交失败，删除目标无法自动恢复；"
                        + "recovery 文件已保留供人工审查。",
                        DeleteRecoveryDirectoryRelativePath,
                        new AggregateException(
                            exception,
                            restoreException));
                }
            }

            throw;
        }
        finally
        {
            var cleanupException = CleanupStagedFiles(stagedFiles);
            if (operationException is null
                && cleanupException is not null)
            {
                ExceptionDispatchInfo
                    .Capture(cleanupException)
                    .Throw();
            }
        }
    }

    private static IReadOnlyList<GeneratedArtifact> ValidateArtifacts(
        IReadOnlyList<GeneratedArtifact> artifacts)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = artifacts
            .Select(artifact =>
            {
                ArgumentNullException.ThrowIfNull(artifact);
                GenerationArtifactPath.Validate(
                    artifact.RelativePath,
                    nameof(artifact.RelativePath));
                RejectInternalPath(artifact.RelativePath);
                ArgumentNullException.ThrowIfNull(artifact.Content);
                if (!paths.Add(artifact.RelativePath))
                {
                    throw new ArgumentException(
                        $"生成产物包含路径别名：{artifact.RelativePath}",
                        nameof(artifacts));
                }

                return artifact;
            })
            .OrderBy(
                artifact => artifact.RelativePath,
                StringComparer.Ordinal)
            .ToArray();
        return ordered;
    }

    private static void ValidateActionPaths(
        IReadOnlyList<GenerationWriteAction> actions)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in actions)
        {
            ArgumentNullException.ThrowIfNull(action);
            GenerationArtifactPath.Validate(
                action.RelativePath,
                nameof(action.RelativePath));
            RejectInternalPath(action.RelativePath);
            if (!paths.Add(action.RelativePath))
            {
                throw new InvalidOperationException(
                    $"生成计划包含路径别名：{action.RelativePath}");
            }
        }
    }

    private static void RejectInternalPath(string relativePath)
    {
        if (StringComparer.OrdinalIgnoreCase.Equals(
                relativePath,
                InternalDirectoryRelativePath)
            || relativePath.StartsWith(
                ".fullnet/",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "生成产物不得占用 .fullnet 内部状态目录。",
                nameof(relativePath));
        }
    }

    private static FileStream OpenWorkspaceLock(string fullRoot)
    {
        var lockPath = GenerationWorkspacePath.Resolve(
            fullRoot,
            LockRelativePath);
        try
        {
            return new FileStream(
                lockPath,
                new FileStreamOptions
                {
                    Mode = FileMode.OpenOrCreate,
                    Access = FileAccess.ReadWrite,
                    Share = FileShare.None,
                    Options = FileOptions.Asynchronous
                        | FileOptions.DeleteOnClose,
                });
        }
        catch (IOException exception)
        {
            throw new GenerationWorkspaceConflictException(
                "另一个生成写盘进程正在占用工作区锁。",
                LockRelativePath,
                exception);
        }
    }

    private static async Task ValidatePlanStateAsync(
        string fullRoot,
        GenerationWritePlan plan,
        CancellationToken cancellationToken)
    {
        foreach (var action in plan.Actions)
        {
            await ValidateActionStateAsync(
                fullRoot,
                action,
                cancellationToken);
        }

        await ValidateManifestStateAsync(
            fullRoot,
            plan.PreviousManifest,
            cancellationToken);
    }

    private static async Task ValidateActionStateAsync(
        string fullRoot,
        GenerationWriteAction action,
        CancellationToken cancellationToken)
    {
        var fullPath = GenerationWorkspacePath.Resolve(
            fullRoot,
            action.RelativePath);
        if (Directory.Exists(fullPath))
        {
            throw Conflict(
                action.RelativePath,
                "生成产物路径已被目录占用。");
        }

        if (action.Kind == GenerationWriteActionKind.Create)
        {
            if (File.Exists(fullPath))
            {
                throw Conflict(
                    action.RelativePath,
                    "计划创建后目标文件已出现。");
            }

            return;
        }

        if (!File.Exists(fullPath))
        {
            throw Conflict(
                action.RelativePath,
                "计划创建后目标文件已消失。");
        }

        var currentContent = await ReadStrictTextAsync(
            fullPath,
            cancellationToken);
        switch (action.Kind)
        {
            case GenerationWriteActionKind.Update:
            case GenerationWriteActionKind.Delete:
                if (!StringComparer.Ordinal.Equals(
                        GenerationContentHash.Compute(currentContent),
                        action.ExistingSha256))
                {
                    throw Conflict(
                        action.RelativePath,
                        "计划写入后目标内容已变化。");
                }

                break;
            case GenerationWriteActionKind.Unchanged:
                if (!StringComparer.Ordinal.Equals(
                        currentContent,
                        action.Content))
                {
                    throw Conflict(
                        action.RelativePath,
                        "计划保持不变的目标内容已变化。");
                }

                break;
            default:
                throw new InvalidOperationException(
                    $"不可应用的生成动作：{action.Kind}");
        }
    }

    private static async Task ValidateManifestStateAsync(
        string fullRoot,
        GenerationManifest? previousManifest,
        CancellationToken cancellationToken)
    {
        var manifestPath = GenerationWorkspacePath.Resolve(
            fullRoot,
            ManifestRelativePath);
        if (Directory.Exists(manifestPath))
        {
            throw Conflict(
                ManifestRelativePath,
                "生成清单路径已被目录占用。");
        }

        if (previousManifest is null)
        {
            if (File.Exists(manifestPath))
            {
                throw Conflict(
                    ManifestRelativePath,
                    "计划创建后生成清单已出现。");
            }

            return;
        }

        if (!File.Exists(manifestPath))
        {
            throw Conflict(
                ManifestRelativePath,
                "计划创建后上一版生成清单已消失。");
        }

        var currentJson = await ReadStrictTextAsync(
            manifestPath,
            cancellationToken);
        GenerationManifest currentManifest;
        try
        {
            currentManifest = GenerationManifest.Parse(currentJson);
        }
        catch (ArgumentException exception)
        {
            throw new GenerationWorkspaceConflictException(
                "磁盘生成清单已损坏或被替换。",
                ManifestRelativePath,
                exception);
        }

        if (!StringComparer.Ordinal.Equals(
                currentManifest.ToJson(),
                previousManifest.ToJson()))
        {
            throw Conflict(
                ManifestRelativePath,
                "上一版生成清单已被并发修改。");
        }
    }

    private static async Task ValidateDesiredStateAsync(
        string fullRoot,
        IReadOnlyList<GenerationWriteAction> actions,
        CancellationToken cancellationToken)
    {
        foreach (var action in actions)
        {
            var fullPath = GenerationWorkspacePath.Resolve(
                fullRoot,
                action.RelativePath);
            if (action.Kind == GenerationWriteActionKind.Delete)
            {
                if (File.Exists(fullPath)
                    || Directory.Exists(fullPath))
                {
                    throw Conflict(
                        action.RelativePath,
                        "产物删除提交后目标路径仍然存在。");
                }

                continue;
            }

            if (!File.Exists(fullPath)
                || Directory.Exists(fullPath))
            {
                throw Conflict(
                    action.RelativePath,
                    "产物提交后目标文件不存在。");
            }

            var currentContent = await ReadStrictTextAsync(
                fullPath,
                cancellationToken);
            if (!StringComparer.Ordinal.Equals(
                    currentContent,
                    action.Content!))
            {
                throw Conflict(
                    action.RelativePath,
                    "提交生成清单前产物内容再次发生变化。");
            }
        }
    }

    private static void CommitArtifact(StagedFile stagedFile)
    {
        if (stagedFile.Action!.Kind == GenerationWriteActionKind.Create)
        {
            File.Move(
                stagedFile.TemporaryPath,
                stagedFile.TargetPath);
            return;
        }

        File.Replace(
            stagedFile.TemporaryPath,
            stagedFile.TargetPath,
            destinationBackupFileName: null);
    }

    private static async Task<ClaimedDeletion> ClaimDeleteAsync(
        string fullRoot,
        GenerationWriteAction action,
        Func<Task>? beforeDeleteClaim,
        CancellationToken cancellationToken)
    {
        if (beforeDeleteClaim is not null)
        {
            await beforeDeleteClaim();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var targetPath = GenerationWorkspacePath.Resolve(
            fullRoot,
            action.RelativePath);
        var identifier = Guid.NewGuid().ToString("N");
        var recoveryRelativePath =
            $"{DeleteRecoveryDirectoryRelativePath}/{identifier}"
            + DeleteRecoveryContentSuffix;
        var metadataRelativePath =
            $"{DeleteRecoveryDirectoryRelativePath}/{identifier}"
            + DeleteRecoveryMetadataSuffix;
        var committedMetadataRelativePath =
            $"{DeleteRecoveryDirectoryRelativePath}/{identifier}"
            + DeleteRecoveryCommittedSuffix;
        GenerationWorkspacePath.EnsureParentDirectory(
            fullRoot,
            recoveryRelativePath);
        var recoveryPath = GenerationWorkspacePath.Resolve(
            fullRoot,
            recoveryRelativePath);
        var metadataPath = GenerationWorkspacePath.Resolve(
            fullRoot,
            metadataRelativePath);
        var committedMetadataPath = GenerationWorkspacePath.Resolve(
            fullRoot,
            committedMetadataRelativePath);
        WriteDeleteRecoveryMetadata(
            metadataPath,
            action.RelativePath);
        try
        {
            // 同卷无覆盖 rename 先声明目录项，之后只校验被声明的文件；
            // 编辑器若重新占用原路径，恢复逻辑绝不覆盖新内容。
            File.Move(targetPath, recoveryPath);
        }
        catch (IOException exception)
        {
            DeleteIfExists(metadataPath);
            throw new GenerationWorkspaceConflictException(
                "无法声明陈旧产物删除空位，磁盘状态已变化。",
                action.RelativePath,
                exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            DeleteIfExists(metadataPath);
            throw new GenerationWorkspaceConflictException(
                "没有权限声明陈旧产物删除空位。",
                action.RelativePath,
                exception);
        }

        var claimedDeletion = new ClaimedDeletion(
            action,
            targetPath,
            recoveryPath,
            metadataPath,
            committedMetadataPath);
        Exception? validationException = null;
        var matchesExpected = false;
        try
        {
            var claimedContent = ReadStrictText(recoveryPath);
            matchesExpected = StringComparer.Ordinal.Equals(
                GenerationContentHash.Compute(claimedContent),
                action.ExistingSha256);
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException
                or DecoderFallbackException)
        {
            validationException = exception;
        }

        if (matchesExpected)
        {
            return claimedDeletion;
        }

        var restoreException = RestoreClaimedDeletion(
            claimedDeletion);
        if (restoreException is not null)
        {
            throw new GenerationWorkspaceConflictException(
                "陈旧产物在删除声明边界发生变化，且原路径已被占用；"
                + "recovery 文件已保留。",
                action.RelativePath,
                validationException is null
                    ? restoreException
                    : new AggregateException(
                        validationException,
                        restoreException));
        }

        throw new GenerationWorkspaceConflictException(
            "陈旧产物在删除声明边界发生变化，已安全恢复。",
            action.RelativePath,
            validationException);
    }

    private static async Task CommitManifestAsync(
        StagedFile stagedFile,
        GenerationManifest? previousManifest,
        Func<Task>? afterManifestClaim,
        Func<Task>? beforeManifestRecoveryCleanup,
        Action onCommitted)
    {
        if (previousManifest is null)
        {
            File.Move(
                stagedFile.TemporaryPath,
                stagedFile.TargetPath);
            onCommitted();
            return;
        }

        var directoryPath = Path.GetDirectoryName(stagedFile.TargetPath)
            ?? throw new InvalidOperationException(
                "生成清单缺少父目录。");
        var displacedManifestPath = Path.Combine(
            directoryPath,
            $"{ManifestRecoveryFilePrefix}{Guid.NewGuid():N}"
            + ManifestRecoveryFileSuffix);
        try
        {
            // 先无覆盖移走真实版本来声明提交空位。此后任何编辑器保存
            // 都会重新占用目标路径，生成器绝不覆盖该版本。
            File.Move(
                stagedFile.TargetPath,
                displacedManifestPath);
        }
        catch (IOException exception)
        {
            throw new GenerationWorkspaceConflictException(
                "无法声明生成清单提交空位，磁盘状态已变化。",
                ManifestRelativePath,
                exception);
        }

        Exception? validationException = null;
        var matchesPrevious = false;
        try
        {
            if (afterManifestClaim is not null)
            {
                await afterManifestClaim();
            }

            // claim 之后不再响应调用方取消；必须完成校验并恢复或提交，
            // 避免把 manifest 留在缺失或未经确认的状态。
            var displacedJson = ReadStrictText(displacedManifestPath);
            var displacedManifest = GenerationManifest.Parse(displacedJson);
            matchesPrevious = StringComparer.Ordinal.Equals(
                displacedManifest.ToJson(),
                previousManifest.ToJson());
        }
        catch (Exception exception)
        {
            validationException = exception;
        }

        if (!matchesPrevious)
        {
            RestoreClaimWithoutOverwrite(
                stagedFile.TargetPath,
                displacedManifestPath,
                validationException);
            throw Conflict(
                ManifestRelativePath,
                "提交边界的上一版生成清单已被并发修改。");
        }

        try
        {
            File.Move(
                stagedFile.TemporaryPath,
                stagedFile.TargetPath);
            onCommitted();
        }
        catch (IOException exception)
        {
            RestoreClaimWithoutOverwrite(
                stagedFile.TargetPath,
                displacedManifestPath,
                exception);
            throw new GenerationWorkspaceConflictException(
                "生成清单提交空位已被并发写入占用。",
                ManifestRelativePath,
                exception);
        }

        try
        {
            if (beforeManifestRecoveryCleanup is not null)
            {
                await beforeManifestRecoveryCleanup();
            }

            File.Delete(displacedManifestPath);
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException)
        {
            var recoveryRelativePath =
                $"{InternalDirectoryRelativePath}/"
                + Path.GetFileName(displacedManifestPath);
            throw new GenerationWorkspaceConflictException(
                "新清单已提交，但旧清单 recovery 清理失败；"
                + "请审查并删除该恢复副本。",
                recoveryRelativePath,
                exception);
        }
    }

    private static void RestoreClaimWithoutOverwrite(
        string targetPath,
        string displacedManifestPath,
        Exception? cause)
    {
        try
        {
            // 只有目标仍为空时才恢复；编辑器已重新保存时绝不覆盖，
            // displaced 文件会保留为显式 recovery 证据。
            File.Move(
                displacedManifestPath,
                targetPath);
        }
        catch (IOException exception)
        {
            var recoveryRelativePath =
                $"{InternalDirectoryRelativePath}/"
                + Path.GetFileName(displacedManifestPath);
            throw new GenerationWorkspaceConflictException(
                "生成清单已被并发写入；先前版本保留在："
                + recoveryRelativePath,
                recoveryRelativePath,
                cause is null
                    ? exception
                    : new AggregateException(cause, exception));
        }
    }

    private static void WriteDeleteRecoveryMetadata(
        string metadataPath,
        string relativePath)
    {
        var bytes = StrictUtf8.GetBytes(relativePath);
        using var stream = new FileStream(
            metadataPath,
            new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.WriteThrough,
            });
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static Exception? RestoreClaimedDeletions(
        IReadOnlyList<ClaimedDeletion> claimedDeletions)
    {
        Exception? firstException = null;
        for (var index = claimedDeletions.Count - 1; index >= 0; index--)
        {
            var restoreException = RestoreClaimedDeletion(
                claimedDeletions[index]);
            firstException ??= restoreException;
        }

        return firstException;
    }

    private static Exception? RestoreClaimedDeletion(
        ClaimedDeletion claimedDeletion)
    {
        try
        {
            if (File.Exists(claimedDeletion.RecoveryPath))
            {
                File.Move(
                    claimedDeletion.RecoveryPath,
                    claimedDeletion.TargetPath);
                DeleteIfExists(claimedDeletion.MetadataPath);
                return null;
            }

            // recovery 缺失时不得把“未完成恢复”误判为成功；只有原路径已恢复为
            // 计划捕获的精确内容，才能移除待恢复阶段证据。
            if (File.Exists(claimedDeletion.TargetPath)
                && StringComparer.Ordinal.Equals(
                    GenerationContentHash.Compute(
                        ReadStrictText(claimedDeletion.TargetPath)),
                    claimedDeletion.Action.ExistingSha256))
            {
                DeleteIfExists(claimedDeletion.MetadataPath);
                return null;
            }

            return new IOException(
                "删除 recovery 已缺失，且原路径未恢复为计划捕获的内容。");
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException
                or DecoderFallbackException)
        {
            return exception;
        }
    }

    private static void CommitDeletionTombstones(
        IReadOnlyList<ClaimedDeletion> claimedDeletions)
    {
        Exception? firstException = null;
        foreach (var claimedDeletion in claimedDeletions)
        {
            try
            {
                // 清单提交后只原子切换阶段标记，恢复副本作为已提交墓碑保留。
                // 自动物理删除无法跨平台证明路径仍指向已校验的同一文件身份。
                File.Move(
                    claimedDeletion.MetadataPath,
                    claimedDeletion.CommittedMetadataPath);
                ValidateCommittedDeletionTombstone(
                    ParseRequiredDeleteRecoveryEntry(
                        claimedDeletion.RecoveryPath),
                    ParseRequiredDeleteRecoveryEntry(
                        claimedDeletion.CommittedMetadataPath),
                    claimedDeletion.Action.RelativePath);
            }
            catch (Exception exception)
                when (exception is GenerationWorkspaceConflictException
                    or IOException
                    or UnauthorizedAccessException)
            {
                firstException ??= new GenerationWorkspaceConflictException(
                    "生成清单已提交，但删除墓碑阶段不完整；"
                    + "recovery 与阶段证据已保留。",
                    claimedDeletion.Action.RelativePath,
                    exception);
            }
        }

        if (firstException is not null)
        {
            ExceptionDispatchInfo.Capture(firstException).Throw();
        }
    }

    private static void ValidateClaimedDeletionsBeforeManifest(
        IReadOnlyList<ClaimedDeletion> claimedDeletions)
    {
        foreach (var claimedDeletion in claimedDeletions)
        {
            try
            {
                var metadata = ReadStrictText(
                    claimedDeletion.MetadataPath);
                var content = ReadStrictText(
                    claimedDeletion.RecoveryPath);
                if (!StringComparer.Ordinal.Equals(
                        metadata,
                        claimedDeletion.Action.RelativePath)
                    || !StringComparer.Ordinal.Equals(
                        GenerationContentHash.Compute(content),
                        claimedDeletion.Action.ExistingSha256))
                {
                    throw Conflict(
                        claimedDeletion.Action.RelativePath,
                        "删除 recovery 在清单提交前发生变化。");
                }
            }
            catch (GenerationWorkspaceConflictException)
            {
                throw;
            }
            catch (Exception exception)
                when (exception is IOException
                    or UnauthorizedAccessException
                    or DecoderFallbackException)
            {
                throw new GenerationWorkspaceConflictException(
                    "无法在清单提交前排除删除 recovery 的并发写入。",
                    claimedDeletion.Action.RelativePath,
                    exception);
            }
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void RejectPendingDeleteRecovery(string fullRoot)
    {
        var recoveryDirectoryPath = GenerationWorkspacePath.Resolve(
            fullRoot,
            DeleteRecoveryDirectoryRelativePath);
        if (!Directory.Exists(recoveryDirectoryPath))
        {
            return;
        }

        var recoveryGroups = Directory
            .EnumerateFileSystemEntries(
                recoveryDirectoryPath,
                "*",
                SearchOption.TopDirectoryOnly)
            .Select(ParseRequiredDeleteRecoveryEntry)
            .Select(entry =>
            {
                ValidateDeleteRecoveryEntryFile(entry);
                return entry;
            })
            .GroupBy(
                entry => entry.Identifier,
                StringComparer.Ordinal)
            .OrderBy(
                group => group.Key,
                StringComparer.Ordinal)
            .ToArray();
        foreach (var recoveryGroup in recoveryGroups)
        {
            var pendingEntries = recoveryGroup
                .Where(entry =>
                    entry.Kind == DeleteRecoveryEntryKind.PendingMetadata)
                .ToArray();
            var recoveryEntries = recoveryGroup
                .Where(entry =>
                    entry.Kind == DeleteRecoveryEntryKind.Recovery)
                .ToArray();
            var committedEntries = recoveryGroup
                .Where(entry =>
                    entry.Kind == DeleteRecoveryEntryKind.CommittedMetadata)
                .ToArray();
            if (pendingEntries.Length == 0
                && recoveryEntries.Length == 1
                && committedEntries.Length == 1
                && recoveryGroup.Count() == 2
                && File.Exists(recoveryEntries[0].FullPath)
                && File.Exists(committedEntries[0].FullPath))
            {
                ValidateCommittedDeletionTombstone(
                    recoveryEntries[0],
                    committedEntries[0]);
                continue;
            }

            var blockingEntry = pendingEntries
                .Concat(recoveryEntries)
                .Concat(committedEntries)
                .OrderBy(
                    entry => entry.FullPath,
                    StringComparer.Ordinal)
                .First();
            var relativePath =
                $"{DeleteRecoveryDirectoryRelativePath}/"
                + Path.GetFileName(blockingEntry.FullPath);
            throw Conflict(
                relativePath,
                "检测到未完成或不完整的产物删除 recovery，请按配对阶段文件审查。");
        }
    }

    private static void ValidateCommittedDeletionTombstone(
        DeleteRecoveryEntry recoveryEntry,
        DeleteRecoveryEntry committedEntry,
        string? expectedRelativePath = null)
    {
        try
        {
            ValidateDeleteRecoveryEntryFile(recoveryEntry);
            ValidateDeleteRecoveryEntryFile(committedEntry);
            var relativePath = ReadStrictText(committedEntry.FullPath);
            GenerationArtifactPath.Validate(
                relativePath,
                nameof(relativePath));
            RejectInternalPath(relativePath);
            if (expectedRelativePath is not null
                && !StringComparer.Ordinal.Equals(
                    relativePath,
                    expectedRelativePath))
            {
                throw new GenerationWorkspaceConflictException(
                    "已提交删除墓碑的原路径与本次删除声明不一致。",
                    relativePath);
            }
        }
        catch (GenerationWorkspaceConflictException)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException
                or DecoderFallbackException
                or ArgumentException)
        {
            var relativePath =
                $"{DeleteRecoveryDirectoryRelativePath}/"
                + Path.GetFileName(committedEntry.FullPath);
            throw new GenerationWorkspaceConflictException(
                "已提交删除墓碑的路径元数据无效，必须人工审查。",
                relativePath,
                exception);
        }
    }

    private static DeleteRecoveryEntry ParseRequiredDeleteRecoveryEntry(
        string path)
    {
        var fileName = Path.GetFileName(path);
        var (suffix, kind) = fileName.EndsWith(
                DeleteRecoveryContentSuffix,
                StringComparison.Ordinal)
            ? (DeleteRecoveryContentSuffix, DeleteRecoveryEntryKind.Recovery)
            : fileName.EndsWith(
                    DeleteRecoveryMetadataSuffix,
                    StringComparison.Ordinal)
                ? (
                    DeleteRecoveryMetadataSuffix,
                    DeleteRecoveryEntryKind.PendingMetadata)
                : fileName.EndsWith(
                        DeleteRecoveryCommittedSuffix,
                        StringComparison.Ordinal)
                    ? (
                        DeleteRecoveryCommittedSuffix,
                        DeleteRecoveryEntryKind.CommittedMetadata)
                    : (null, DeleteRecoveryEntryKind.Unknown);
        if (suffix is null
            || !Guid.TryParseExact(
                fileName[..^suffix.Length],
                "N",
                out var identifier))
        {
            throw InvalidDeleteRecoveryEntry(path);
        }

        var canonicalFileName =
            identifier.ToString("N") + suffix;
        if (!StringComparer.Ordinal.Equals(
                fileName,
                canonicalFileName))
        {
            throw InvalidDeleteRecoveryEntry(path);
        }

        return new DeleteRecoveryEntry(
            identifier.ToString("N"),
            kind,
            path);
    }

    private static void ValidateDeleteRecoveryEntryFile(
        DeleteRecoveryEntry entry)
    {
        try
        {
            var attributes = File.GetAttributes(entry.FullPath);
            if ((attributes & (
                    FileAttributes.Directory
                    | FileAttributes.ReparsePoint)) != 0)
            {
                throw InvalidDeleteRecoveryEntry(entry.FullPath);
            }
        }
        catch (GenerationWorkspaceConflictException)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException)
        {
            var relativePath =
                $"{DeleteRecoveryDirectoryRelativePath}/"
                + Path.GetFileName(entry.FullPath);
            throw new GenerationWorkspaceConflictException(
                "删除 recovery 条目缺失或无法验证，必须人工审查。",
                relativePath,
                exception);
        }
    }

    private static GenerationWorkspaceConflictException
        InvalidDeleteRecoveryEntry(string path)
    {
        var relativePath =
            $"{DeleteRecoveryDirectoryRelativePath}/"
            + Path.GetFileName(path);
        return Conflict(
            relativePath,
            "删除 recovery 目录包含非规范或非普通文件条目。");
    }

    private static void RejectPendingManifestRecovery(string fullRoot)
    {
        var internalDirectoryPath = GenerationWorkspacePath.Resolve(
            fullRoot,
            InternalDirectoryRelativePath);
        if (!Directory.Exists(internalDirectoryPath))
        {
            return;
        }

        var recoveryPath = Directory
            .EnumerateFileSystemEntries(
                internalDirectoryPath,
                "*",
                SearchOption.TopDirectoryOnly)
            .Where(IsManifestRecoveryPath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .FirstOrDefault();
        if (recoveryPath is null)
        {
            return;
        }

        var relativePath =
            $"{InternalDirectoryRelativePath}/{Path.GetFileName(recoveryPath)}";
        GenerationWorkspacePath.Resolve(fullRoot, relativePath);
        throw Conflict(
            relativePath,
            "检测到尚未清理的生成清单 recovery；"
            + "清单提交可能已完成或中断，请先审查。");
    }

    private static bool IsManifestRecoveryPath(string path)
    {
        var fileName = Path.GetFileName(path);
        if (!fileName.StartsWith(
                ManifestRecoveryFilePrefix,
                StringComparison.OrdinalIgnoreCase)
            || !fileName.EndsWith(
                ManifestRecoveryFileSuffix,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var identifier = fileName[
            ManifestRecoveryFilePrefix.Length
            ..^ManifestRecoveryFileSuffix.Length];
        return Guid.TryParseExact(
            identifier,
            "N",
            out _);
    }

    private static async Task<GenerationManifest?> ReadManifestAsync(
        string fullRoot,
        CancellationToken cancellationToken)
    {
        var manifestPath = GenerationWorkspacePath.Resolve(
            fullRoot,
            ManifestRelativePath);
        if (Directory.Exists(manifestPath))
        {
            throw Conflict(
                ManifestRelativePath,
                "生成清单路径已被目录占用。");
        }

        if (!File.Exists(manifestPath))
        {
            return null;
        }

        var json = await ReadStrictTextAsync(
            manifestPath,
            cancellationToken);
        try
        {
            var manifest = GenerationManifest.Parse(json);
            foreach (var entry in manifest.Artifacts)
            {
                RejectInternalPath(entry.RelativePath);
            }

            return manifest;
        }
        catch (ArgumentException exception)
        {
            throw new GenerationWorkspaceConflictException(
                "磁盘生成清单已损坏或被替换。",
                ManifestRelativePath,
                exception);
        }
    }

    private static async Task<string> ReadStrictTextAsync(
        string fullPath,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(
            fullPath,
            cancellationToken);
        return DecodeStrictText(bytes);
    }

    private static string ReadStrictText(string fullPath)
    {
        return DecodeStrictText(File.ReadAllBytes(fullPath));
    }

    private static string DecodeStrictText(byte[] bytes)
    {
        if (bytes.Length >= 3
            && bytes[0] == 0xEF
            && bytes[1] == 0xBB
            && bytes[2] == 0xBF)
        {
            throw new DecoderFallbackException(
                "生成工作区文本不得包含 UTF-8 BOM。");
        }

        return StrictUtf8.GetString(bytes);
    }

    private static async Task<string> StageTextAsync(
        string targetPath,
        string content,
        CancellationToken cancellationToken)
    {
        var directoryPath = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException(
                "生成目标缺少父目录。");
        var temporaryPath = Path.Combine(
            directoryPath,
            $".fullnet-codegeneration-{Guid.NewGuid():N}.tmp");
        try
        {
            if (content.Length > 0
                && content[0] == '\uFEFF')
            {
                throw new EncoderFallbackException(
                    "生成文本不得以 Unicode BOM 字符开头。");
            }

            var bytes = StrictUtf8.GetBytes(content);
            await using var stream = new FileStream(
                temporaryPath,
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options = FileOptions.Asynchronous
                        | FileOptions.WriteThrough,
                });
            await stream.WriteAsync(bytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Flush(flushToDisk: true);
            return temporaryPath;
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            throw;
        }
    }

    private static Exception? CleanupStagedFiles(
        IReadOnlyList<StagedFile> stagedFiles)
    {
        Exception? firstException = null;
        foreach (var stagedFile in stagedFiles)
        {
            try
            {
                if (File.Exists(stagedFile.TemporaryPath))
                {
                    File.Delete(stagedFile.TemporaryPath);
                }
            }
            catch (Exception exception)
            {
                firstException ??= exception;
            }
        }

        return firstException;
    }

    private static GenerationWorkspaceConflictException Conflict(
        string relativePath,
        string reason)
    {
        return new GenerationWorkspaceConflictException(
            $"{reason} 路径：{relativePath}",
            relativePath);
    }

    private sealed record StagedFile(
        GenerationWriteAction? Action,
        string TargetPath,
        string TemporaryPath,
        bool IsManifest);

    private sealed record ClaimedDeletion(
        GenerationWriteAction Action,
        string TargetPath,
        string RecoveryPath,
        string MetadataPath,
        string CommittedMetadataPath);

    private sealed record DeleteRecoveryEntry(
        string Identifier,
        DeleteRecoveryEntryKind Kind,
        string FullPath);

    private enum DeleteRecoveryEntryKind
    {
        Unknown = 0,
        Recovery = 1,
        PendingMetadata = 2,
        CommittedMetadata = 3,
    }
}
