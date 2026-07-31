using System.Collections.ObjectModel;
using System.Text;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.CodeGeneration.Integration;
using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.CodeGeneration.Cli;

/// <summary>
/// 保存 Composition 项目引用、Catalog、编译门禁和提交结果。
/// </summary>
internal sealed class CompositionIntegrationApplyResult
{
    internal CompositionIntegrationApplyResult(
        bool applied,
        bool projectChanged,
        bool catalogChanged,
        ModuleIntegrationCompilationResult? compilation,
        IEnumerable<string> diagnostics)
    {
        Applied = applied;
        ProjectChanged = projectChanged;
        CatalogChanged = catalogChanged;
        Compilation = compilation;
        Diagnostics = new ReadOnlyCollection<string>(
            diagnostics.ToArray());
    }

    public bool Applied { get; }

    public bool ProjectChanged { get; }

    public bool CatalogChanged { get; }

    public ModuleIntegrationCompilationResult? Compilation { get; }

    public IReadOnlyList<string> Diagnostics { get; }
}

/// <summary>
/// 在模块接线、候选 Composition 编译与并发复核通过后提交两个显式手写文件。
/// </summary>
internal static class CompositionIntegrationApplyCommand
{
    private const string ModuleWorkspaceLockRelativePath =
        ".fullnet/codegeneration.lock";
    private const string CompositionLockRelativePath =
        ".fullnet/codegeneration-composition.lock";

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static async Task<CompositionIntegrationApplyResult> ApplyAsync(
        string repositoryRoot,
        FullNetCrudSchema schema,
        ModuleIntegrationTarget target,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(repositoryRoot);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException();
        }

        if (!MatchesModule(schema.RootNamespace, target.ModuleName))
        {
            return Failure(
                "Schema 根命名空间与显式目标模块不匹配。");
        }

        var moduleProject = Resolve(root, target.ModuleProjectPath);
        if (!File.Exists(moduleProject))
        {
            return Failure("模块项目不存在，无法接入 Composition。");
        }

        var moduleRoot = Path.GetDirectoryName(moduleProject)
            ?? throw new InvalidOperationException(
                "模块项目缺少父目录。");
        var moduleEntry = Resolve(root, target.ModuleEntryPointPath);
        var compositionProject = Resolve(
            root,
            target.CompositionProjectPath);
        var compositionCatalog = Resolve(
            root,
            target.CompositionCatalogPath);
        if (!File.Exists(moduleEntry))
        {
            return Failure(
                "模块入口不存在，请先完成模块入口接线。");
        }

        if (!File.Exists(compositionProject)
            || !File.Exists(compositionCatalog))
        {
            return Failure(
                "Composition 项目或 Catalog 不存在，拒绝猜测创建。");
        }

        var registryFailure =
            await ModuleEntryIntegrationApplyCommand
                .ValidateRegistryOwnershipAsync(
                    moduleRoot,
                    cancellationToken);
        if (registryFailure is not null)
        {
            return Failure(registryFailure);
        }

        var moduleEntryContent = await ReadStrictTextAsync(
            moduleEntry,
            cancellationToken);
        var moduleEntryEdit = ModuleEntryIntegrationEditor.Edit(
            moduleEntryContent,
            schema.RootNamespace);
        if (!moduleEntryEdit.Succeeded || moduleEntryEdit.Changed)
        {
            return Failure(
                "模块入口尚未完成稳定聚合调用，请先执行 apply-module-entry-integration。");
        }

        var originalProject = await ReadStrictTextAsync(
            compositionProject,
            cancellationToken);
        var originalCatalog = await ReadStrictTextAsync(
            compositionCatalog,
            cancellationToken);
        var projectEdit = CompositionProjectEditor.Edit(
            originalProject,
            target.CompositionProjectPath,
            target.ModuleProjectPath);
        var catalogEdit = CompositionCatalogEditor.Edit(
            originalCatalog,
            schema.RootNamespace,
            target.ModuleName);
        if (!projectEdit.Succeeded || !catalogEdit.Succeeded)
        {
            return new CompositionIntegrationApplyResult(
                applied: false,
                projectChanged: false,
                catalogChanged: false,
                compilation: null,
                projectEdit.Diagnostics.Concat(
                    catalogEdit.Diagnostics));
        }

        if (!projectEdit.Changed && !catalogEdit.Changed)
        {
            return new CompositionIntegrationApplyResult(
                applied: true,
                projectChanged: false,
                catalogChanged: false,
                compilation: null,
                diagnostics: []);
        }

        var compilation =
            await CompositionIntegrationCompilationCommand.ValidateAsync(
                root,
                compositionProject,
                moduleProject,
                compositionCatalog,
                catalogEdit.DesiredContent,
                includeModuleReference: projectEdit.Changed,
                cancellationToken);
        if (!compilation.Succeeded)
        {
            return new CompositionIntegrationApplyResult(
                applied: false,
                projectChanged: false,
                catalogChanged: false,
                compilation,
                diagnostics: []);
        }

        await CommitAsync(
            root,
            moduleRoot,
            moduleEntry,
            moduleEntryContent,
            compositionProject,
            originalProject,
            projectEdit,
            compositionCatalog,
            originalCatalog,
            catalogEdit,
            cancellationToken);
        return new CompositionIntegrationApplyResult(
            applied: true,
            projectEdit.Changed,
            catalogEdit.Changed,
            compilation,
            diagnostics: []);
    }

    private static async Task CommitAsync(
        string repositoryRoot,
        string moduleRoot,
        string moduleEntryPath,
        string expectedModuleEntry,
        string compositionProjectPath,
        string originalProject,
        CompositionIntegrationEditResult projectEdit,
        string compositionCatalogPath,
        string originalCatalog,
        CompositionIntegrationEditResult catalogEdit,
        CancellationToken cancellationToken)
    {
        var compositionLockPath = Path.Combine(
            repositoryRoot,
            CompositionLockRelativePath.Replace(
                '/',
                Path.DirectorySeparatorChar));
        var moduleLockPath = Path.Combine(
            moduleRoot,
            ModuleWorkspaceLockRelativePath.Replace(
                '/',
                Path.DirectorySeparatorChar));
        Directory.CreateDirectory(
            Path.GetDirectoryName(compositionLockPath)!);
        Directory.CreateDirectory(
            Path.GetDirectoryName(moduleLockPath)!);
        await using var compositionLock = OpenLock(
            compositionLockPath,
            CompositionLockRelativePath);
        await using var moduleLock = OpenLock(
            moduleLockPath,
            ModuleWorkspaceLockRelativePath);

        var registryFailure =
            await ModuleEntryIntegrationApplyCommand
                .ValidateRegistryOwnershipAsync(
                    moduleRoot,
                    cancellationToken);
        if (registryFailure is not null)
        {
            throw new GenerationWorkspaceConflictException(
                registryFailure,
                ModuleIntegrationBackendWorkspace.RegistryRelativePath);
        }

        await EnsureUnchangedAsync(
            moduleEntryPath,
            expectedModuleEntry,
            "候选编译后模块入口发生变化。",
            cancellationToken);
        await EnsureUnchangedAsync(
            compositionProjectPath,
            originalProject,
            "候选编译后 Composition 项目发生变化。",
            cancellationToken);
        await EnsureUnchangedAsync(
            compositionCatalogPath,
            originalCatalog,
            "候选编译后 Composition Catalog 发生变化。",
            cancellationToken);

        string? stagedProject = null;
        string? projectRecovery = null;
        string? stagedCatalog = null;
        var projectCommitted = false;
        try
        {
            stagedProject = projectEdit.Changed
                ? await StageTextAsync(
                    compositionProjectPath,
                    projectEdit.DesiredContent,
                    cancellationToken)
                : null;
            projectRecovery = projectEdit.Changed
                ? await StageTextAsync(
                    compositionProjectPath,
                    originalProject,
                    cancellationToken)
                : null;
            stagedCatalog = catalogEdit.Changed
                ? await StageTextAsync(
                    compositionCatalogPath,
                    catalogEdit.DesiredContent,
                    cancellationToken)
                : null;
            cancellationToken.ThrowIfCancellationRequested();
            if (stagedProject is not null)
            {
                File.Move(
                    stagedProject,
                    compositionProjectPath,
                    overwrite: true);
                stagedProject = null;
                projectCommitted = true;
            }

            if (stagedCatalog is not null)
            {
                File.Move(
                    stagedCatalog,
                    compositionCatalogPath,
                    overwrite: true);
                stagedCatalog = null;
            }
        }
        catch (Exception commitException)
        {
            if (projectCommitted
                && projectRecovery is not null
                && File.Exists(projectRecovery))
            {
                try
                {
                    File.Move(
                        projectRecovery,
                        compositionProjectPath,
                        overwrite: true);
                    projectRecovery = null;
                }
                catch (Exception recoveryException)
                    when (recoveryException is IOException
                        or UnauthorizedAccessException)
                {
                    var preservedRecovery = projectRecovery;
                    projectRecovery = null;
                    throw new GenerationWorkspaceConflictException(
                        "Composition Catalog 提交失败且项目回滚失败；"
                        + "原项目恢复副本已保留，必须人工审查。",
                        Path.GetFileName(preservedRecovery),
                        new AggregateException(
                            commitException,
                            recoveryException));
                }
            }

            throw;
        }
        finally
        {
            DeleteIfExists(stagedProject);
            DeleteIfExists(stagedCatalog);
            DeleteIfExists(projectRecovery);
        }
    }

    private static async Task EnsureUnchangedAsync(
        string path,
        string expectedContent,
        string reason,
        CancellationToken cancellationToken)
    {
        var current = await ReadStrictTextAsync(path, cancellationToken);
        if (!StringComparer.Ordinal.Equals(current, expectedContent))
        {
            throw new GenerationWorkspaceConflictException(
                reason,
                Path.GetFileName(path));
        }
    }

    private static FileStream OpenLock(
        string path,
        string relativePath)
    {
        try
        {
            return new FileStream(
                path,
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
                relativePath,
                exception);
        }
    }

    private static async Task<string> StageTextAsync(
        string targetPath,
        string content,
        CancellationToken cancellationToken)
    {
        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(targetPath)!,
            $".fullnet-composition-{Guid.NewGuid():N}.tmp");
        try
        {
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
            var bytes = StrictUtf8.GetBytes(content);
            await stream.WriteAsync(bytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Flush(flushToDisk: true);
            return temporaryPath;
        }
        catch
        {
            DeleteIfExists(temporaryPath);
            throw;
        }
    }

    private static async Task<string> ReadStrictTextAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(
            path,
            cancellationToken);
        if (bytes.Length >= 3
            && bytes[0] == 0xEF
            && bytes[1] == 0xBB
            && bytes[2] == 0xBF)
        {
            throw new DecoderFallbackException(
                "Composition 接入文本不得包含 UTF-8 BOM。");
        }

        return StrictUtf8.GetString(bytes);
    }

    private static void DeleteIfExists(string? path)
    {
        if (path is not null && File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string Resolve(
        string repositoryRoot,
        string relativePath) =>
        Path.GetFullPath(Path.Combine(
            repositoryRoot,
            relativePath.Replace(
                '/',
                Path.DirectorySeparatorChar)));

    private static bool MatchesModule(
        string rootNamespace,
        string moduleName) =>
        StringComparer.Ordinal.Equals(rootNamespace, moduleName)
        || rootNamespace.EndsWith(
            $".{moduleName}",
            StringComparison.Ordinal);

    private static CompositionIntegrationApplyResult Failure(
        string diagnostic) =>
        new(
            applied: false,
            projectChanged: false,
            catalogChanged: false,
            ModuleIntegrationCompilationResult.Failure([diagnostic]),
            diagnostics: []);
}
