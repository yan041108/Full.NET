using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.Data.CodeGeneration.Integration;

/// <summary>
/// Host Apply 在检查点之后编排既有模块/Composition/Vue 接入命令，编译失败则零写入。
/// </summary>
public static class ModuleIntegrationHostOrchestrator
{
    /// <summary>
    /// 按显式目标执行整条接入链：后端→入口→Composition→Vue→AuthorizationContributor；
    /// 任一子命令失败立即返回，不继续写盘后续文件。
    /// </summary>
    /// <param name="repositoryRoot">仓库根目录绝对路径</param>
    /// <param name="schema">待接入实体的 CRUD Schema</param>
    /// <param name="target">显式声明的模块接入目标</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>整条接入链的稳定结果；官方 Full.NET.Modules.* 直接拒绝</returns>
    public static async Task<ModuleIntegrationHostApplyResult> ApplyAsync(
        string repositoryRoot,
        FullNetCrudSchema schema,
        ModuleIntegrationTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(target);

        if (target.ModuleProjectPath.Contains(
                "Full.NET.Modules.",
                StringComparison.Ordinal))
        {
            return ModuleIntegrationHostApplyResult.Failure(
                "禁止对官方 Full.NET.Modules.* 做隐式推断接入。");
        }

        // 整链首步前先拒绝已有恢复现场，避免后端或入口先写入后才在 Composition 阶段阻断。
        try
        {
            var root = GenerationWorkspacePath.NormalizeRoot(repositoryRoot);
            CompositionIntegrationRecovery.RejectPending(root,
                Path.Combine(root, target.CompositionProjectPath),
                Path.Combine(root, target.CompositionCatalogPath));
            if (target.AuthorizationContributorPath is not null)
            {
                RejectAuthorizationPending(root, target.AuthorizationContributorPath);
                ResolveAuthorizationContributor(root, target.AuthorizationContributorPath);
            }
        }
        catch (GenerationWorkspaceConflictException exception)
        {
            return ModuleIntegrationHostApplyResult.Failure(exception.Message);
        }

        var backend = await ModuleIntegrationBackendApplyCommand
            .ApplyAsync(repositoryRoot, schema, target, cancellationToken)
            .ConfigureAwait(false);
        if (!backend.Applied)
        {
            return ModuleIntegrationHostApplyResult.Failure(
                backend.Compilation?.Diagnostics
                    ?? ["模块后端接入编译失败，未写入业务文件。"]);
        }

        var entry = await ModuleEntryIntegrationApplyCommand
            .ApplyAsync(repositoryRoot, schema, target, cancellationToken)
            .ConfigureAwait(false);
        if (!entry.Applied)
        {
            return ModuleIntegrationHostApplyResult.Failure(
                entry.Diagnostics);
        }

        var composition = await CompositionIntegrationApplyCommand
            .ApplyAsync(repositoryRoot, schema, target, cancellationToken)
            .ConfigureAwait(false);
        if (!composition.Applied)
        {
            return ModuleIntegrationHostApplyResult.Failure(
                composition.Diagnostics);
        }

        if (target.ClientRoute is not null)
        {
            try
            {
                await WriteVueViewAsync(
                    repositoryRoot,
                    schema,
                    target.ClientRoute,
                    cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (GenerationWorkspaceConflictException exception)
            {
                return ModuleIntegrationHostApplyResult.Failure(exception.Message);
            }
            var routes = await ClientRouteIntegrationApplyCommand
                .ApplyAsync(repositoryRoot, schema, target, cancellationToken)
                .ConfigureAwait(false);
            if (!routes.Applied)
            {
                return ModuleIntegrationHostApplyResult.Failure(
                    routes.Diagnostics);
            }
        }

        if (target.AuthorizationContributorPath is not null)
        {
            try
            {
                var root = GenerationWorkspacePath.NormalizeRoot(repositoryRoot);
                var contributorFullPath = ResolveAuthorizationContributor(root, target.AuthorizationContributorPath);

                var original = await File.ReadAllTextAsync(
                        contributorFullPath,
                        cancellationToken)
                    .ConfigureAwait(false);
                var fragment = CrudAuthorizationContributorFragmentGenerator
                    .Generate(schema);
                var edited = AuthorizationContributorIntegrationEditor.Edit(
                    original,
                    target.AuthorizationContributorPath,
                    fragment);
                if (!edited.Succeeded)
                {
                    return ModuleIntegrationHostApplyResult.Failure(
                        edited.Diagnostics);
                }

                if (edited.Changed)
                {
                    // 前置检查不能替代最终写入边界，重新拒绝期间出现的链接、别名或目录占用。
                    await CommitAuthorizationContributorAsync(root, target.AuthorizationContributorPath,
                            original, edited.DesiredContent, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (GenerationWorkspaceConflictException exception)
            {
                return ModuleIntegrationHostApplyResult.Failure(exception.Message);
            }
        }

        return ModuleIntegrationHostApplyResult.Success();
    }

    internal static async Task CommitAuthorizationContributorAsync(
        string repositoryRoot, string relativePath, string original, string desired,
        CancellationToken cancellationToken, Func<Task>? afterStaging = null)
    {
        var root = GenerationWorkspacePath.NormalizeRoot(repositoryRoot);
        RejectAuthorizationPending(root, relativePath);
        const string lockRelative = ".fullnet/codegeneration-authorization.lock";
        GenerationWorkspacePath.ResolveFile(root, lockRelative);
        GenerationWorkspacePath.EnsureParentDirectory(root, lockRelative);
        var lockPath = GenerationWorkspacePath.ResolveFile(root, lockRelative);
        FileStream workspaceLock;
        try
        {
            workspaceLock = new FileStream(lockPath, FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        }
        catch (IOException exception)
        {
            throw new GenerationWorkspaceConflictException("另一个授权接入进程正在占用工作区锁。", lockRelative, exception);
        }

        await using var heldLock = workspaceLock;
        RejectAuthorizationPending(root, relativePath);
        var path = await ValidateAuthorizationOriginalAsync(root, relativePath, original, cancellationToken);
        var temporaryRelative = Path.GetRelativePath(root, Path.Combine(Path.GetDirectoryName(path)!,
            $".fullnet-authorization-{Guid.NewGuid():N}.tmp")).Replace(Path.DirectorySeparatorChar, '/');
        var temporaryPath = GenerationWorkspacePath.ResolveFile(root, temporaryRelative);
        var desiredBytes = System.Text.Encoding.UTF8.GetBytes(desired);
        Exception? commitFailure = null;
        var stagingCompleted = false;
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(desiredBytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
                stagingCompleted = true;
            }

            if (afterStaging is not null) await afterStaging();
            // 暂存期间的人工修改同样不能覆盖；最终路径仍受原工作区边界约束。
            path = await ValidateAuthorizationOriginalAsync(root, relativePath, original, cancellationToken);
            GenerationWorkspacePath.RevalidateFile(root, temporaryPath);
            var stagedBytes = await File.ReadAllBytesAsync(temporaryPath, cancellationToken);
            if (!stagedBytes.AsSpan().SequenceEqual(desiredBytes))
            {
                throw new GenerationWorkspaceConflictException("授权暂存材料发生漂移，必须人工审查。", temporaryRelative);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch (Exception exception)
        {
            commitFailure = exception;
            throw;
        }
        finally
        {
            // 父目录若被替换，保留现场而不沿新链接删除工作区外文件；漂移材料也不能当作本次内容清理。
            string? safeTemporary = null;
            try
            {
                safeTemporary = GenerationWorkspacePath.ResolveFile(root, temporaryRelative);
            }
            catch (GenerationWorkspaceConflictException) { }
            // 未完成写入的半成品保留，不把原始 I/O 或取消误报成“人工漂移”。
            if (stagingCompleted && safeTemporary is not null && File.Exists(safeTemporary))
            {
                try
                {
                    var remaining = await File.ReadAllBytesAsync(safeTemporary, CancellationToken.None);
                    if (!remaining.AsSpan().SequenceEqual(desiredBytes))
                    {
                        throw new GenerationWorkspaceConflictException(
                            "授权暂存材料发生漂移，材料已保留，必须人工审查。", temporaryRelative, commitFailure);
                    }
                    File.Delete(safeTemporary);
                }
                catch (GenerationWorkspaceConflictException) { throw; }
                catch (Exception cleanupFailure) when (cleanupFailure is IOException or UnauthorizedAccessException)
                {
                    // 清理失败不得丢失先前提交原因；保留材料并向 Host 返回受控冲突。
                    throw new GenerationWorkspaceConflictException(
                        "授权暂存材料清理失败，材料已保留，必须人工审查。", temporaryRelative,
                        commitFailure is null ? cleanupFailure : new AggregateException(commitFailure, cleanupFailure));
                }
            }
        }
    }

    private static void RejectAuthorizationPending(string root, string relativePath)
    {
        var target = GenerationWorkspacePath.ResolveFile(root, relativePath);
        var parent = Path.GetDirectoryName(target)!;
        if (!Directory.Exists(parent)) return;
        // 未完成暂存、漂移和清理失败都可能留下材料；只枚举目录项，不读取或跟随残留链接。
        var pending = Directory.EnumerateFileSystemEntries(parent).FirstOrDefault(path =>
            Path.GetFileName(path).StartsWith(".fullnet-authorization-", StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(path).EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
        if (pending is not null)
        {
            throw new GenerationWorkspaceConflictException("授权贡献者存在待审查的暂存材料，拒绝重新接入。",
                Path.GetRelativePath(root, pending).Replace(Path.DirectorySeparatorChar, '/'));
        }
    }

    private static async Task<string> ValidateAuthorizationOriginalAsync(
        string root, string relativePath, string original, CancellationToken cancellationToken)
    {
        var path = ResolveAuthorizationContributor(root, relativePath);
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        // 字节一致才允许替换，非法编码或 BOM 不因文本解码归一化而被静默改写。
        if (!bytes.AsSpan().SequenceEqual(System.Text.Encoding.UTF8.GetBytes(original)))
        {
            throw new GenerationWorkspaceConflictException("授权贡献者发生并发变化或编码漂移，拒绝覆盖。", relativePath);
        }

        return path;
    }

    private static string ResolveAuthorizationContributor(string root, string relativePath)
    {
        var path = GenerationWorkspacePath.ResolveFile(root, relativePath);
        if (!File.Exists(path))
        {
            throw new GenerationWorkspaceConflictException(
                "显式 AuthorizationContributor 文件不存在。", relativePath);
        }

        return path;
    }

    private static async Task WriteVueViewAsync(
        string repositoryRoot,
        FullNetCrudSchema schema,
        ModuleClientRouteTarget route,
        CancellationToken cancellationToken)
    {
        var artifacts = CrudArtifactGenerator.Generate(schema);
        var directory = route.VueComponentPath.Contains('/', StringComparison.Ordinal)
            ? route.VueComponentPath[..route.VueComponentPath.LastIndexOf('/')]
            : string.Empty;
        var view = artifacts.Single(artifact => artifact.Kind == GeneratedArtifactKind.VueView);
        var mapped = new List<GeneratedArtifact>
        {
            new(route.VueComponentPath, view.Kind, view.Content),
        };
        mapped.AddRange(artifacts.Where(artifact => artifact.Kind == GeneratedArtifactKind.VueClient
                && artifact.RelativePath.EndsWith(".generated.ts", StringComparison.Ordinal))
            .Select(artifact => new GeneratedArtifact(
                string.IsNullOrEmpty(directory) ? Path.GetFileName(artifact.RelativePath)
                    : directory + "/" + Path.GetFileName(artifact.RelativePath),
                artifact.Kind, artifact.Content)));
        var snapshot = await GenerationWorkspaceStore.CaptureAsync(
            repositoryRoot, mapped, cancellationToken).ConfigureAwait(false);
        var desired = mapped.ToDictionary(artifact => artifact.RelativePath, artifact => artifact.Content,
            StringComparer.Ordinal);

        // Vue 接入是增量批次，必须保留其他实体和生成器已经拥有的产物，禁止将它们误判为删除。
        foreach (var previous in snapshot.PreviousManifest?.Artifacts ?? [])
        {
            if (desired.ContainsKey(previous.RelativePath)) continue;
            if (!snapshot.ExistingFiles.TryGetValue(previous.RelativePath, out var content)
                || !StringComparer.Ordinal.Equals(GenerationContentHash.Compute(content), previous.Sha256))
            {
                throw new GenerationWorkspaceConflictException(
                    $"已有生成产物缺失或被人工修改：{previous.RelativePath}", previous.RelativePath);
            }

            desired.Add(previous.RelativePath, content);
        }

        var plan = GenerationWritePlanner.PlanFromDesiredContents(
            desired, snapshot.ExistingFiles, snapshot.PreviousManifest);
        if (!plan.CanApply)
        {
            var conflict = plan.Actions.First(action => action.Kind == GenerationWriteActionKind.Conflict);
            throw new GenerationWorkspaceConflictException(
                $"Vue 生成产物存在人工修改或所有权冲突：{conflict.RelativePath}", conflict.RelativePath);
        }

        // 复用排他锁、快照复核与清单最后提交；失败时安全恢复，人工冲突保留证据等待审查。
        await GenerationWorkspaceStore.ApplyAsync(repositoryRoot, plan, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Host 接入链的稳定结果；失败时不得把诊断写成成功。</summary>
public sealed class ModuleIntegrationHostApplyResult
{
    private ModuleIntegrationHostApplyResult(
        bool succeeded,
        IReadOnlyList<string> diagnostics)
    {
        Succeeded = succeeded;
        Diagnostics = diagnostics;
    }

    /// <summary>整条接入链是否全部成功；任一子命令失败即为 false。</summary>
    public bool Succeeded { get; }

    /// <summary>失败时返回子命令的诊断信息；成功时为空。</summary>
    public IReadOnlyList<string> Diagnostics { get; }

    /// <summary>构造一个接入链成功结果，诊断集合为空。</summary>
    public static ModuleIntegrationHostApplyResult Success() =>
        new(true, []);

    /// <summary>构造一个接入链失败结果，必须至少包含一条诊断。</summary>
    public static ModuleIntegrationHostApplyResult Failure(
        IEnumerable<string> diagnostics) =>
        new(false, diagnostics.ToArray());

    /// <summary>构造一个接入链失败结果，包含单条诊断。</summary>
    public static ModuleIntegrationHostApplyResult Failure(string diagnostic) =>
        Failure([diagnostic]);
}
