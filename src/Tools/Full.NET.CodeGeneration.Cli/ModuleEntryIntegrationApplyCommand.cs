using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.CodeGeneration.Integration;
using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.CodeGeneration.Cli;

/// <summary>
/// 保存模块入口接线的源码状态、编译门禁和提交结果。
/// </summary>
internal sealed class ModuleEntryIntegrationApplyResult
{
    internal ModuleEntryIntegrationApplyResult(
        bool applied,
        bool changed,
        ModuleIntegrationCompilationResult? compilation,
        IEnumerable<string> diagnostics)
    {
        Applied = applied;
        Changed = changed;
        Compilation = compilation;
        Diagnostics = new ReadOnlyCollection<string>(
            diagnostics.ToArray());
    }

    public bool Applied { get; }

    public bool Changed { get; }

    public ModuleIntegrationCompilationResult? Compilation { get; }

    public IReadOnlyList<string> Diagnostics { get; }
}

/// <summary>
/// 在聚合桥所有权、候选编译和并发复核全部通过后，显式更新手写模块入口。
/// </summary>
internal static class ModuleEntryIntegrationApplyCommand
{
    private const string WorkspaceLockRelativePath =
        ".fullnet/codegeneration.lock";

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    /// <summary>
    /// 在聚合桥所有权校验、模块入口候选编译与并发复核全部通过后，原子更新手写模块入口文件；任一前置检查失败均返回未提交结果而非抛异常。
    /// </summary>
    /// <remarks>
    /// 安全顺序：先验证 Schema 与目标模块匹配、再校验聚合桥未被并发漂移、再候选编译验证、最后在工作区独占锁下重新校验聚合桥并按字节序一致性写入。
    /// 写入采用临时文件 + 原子 Move + WriteThrough，避免崩溃导致入口文件半截。
    /// </remarks>
    /// <param name="repositoryRoot">仓库根目录，用于解析所有相对路径。</param>
    /// <param name="schema">CRUD Schema，提供根命名空间与生成器入口签名。</param>
    /// <param name="target">模块接入目标，提供模块项目与入口文件相对路径。</param>
    /// <param name="cancellationToken">用于取消文件 IO、编译进程与锁操作的令牌。</param>
    /// <returns>提交结果，包含是否已应用、是否发生改变、候选编译诊断与失败诊断。</returns>
    public static async Task<ModuleEntryIntegrationApplyResult> ApplyAsync(
        string repositoryRoot,
        FullNetCrudSchema schema,
        ModuleIntegrationTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(target);

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

        var moduleProjectFullPath = Resolve(root, target.ModuleProjectPath);
        if (!File.Exists(moduleProjectFullPath))
        {
            return Failure(
                "模块项目不存在，无法执行编译验证。");
        }

        var moduleRoot = Path.GetDirectoryName(moduleProjectFullPath)
            ?? throw new InvalidOperationException(
                "模块项目缺少父目录。");
        var moduleEntryFullPath = Resolve(
            root,
            target.ModuleEntryPointPath);
        if (!IsWithin(moduleRoot, moduleEntryFullPath))
        {
            return Failure(
                "模块入口必须位于显式模块项目目录内。");
        }

        if (!File.Exists(moduleEntryFullPath))
        {
            return Failure(
                "模块入口不存在，拒绝猜测或创建手写入口。");
        }

        var registryFailure = await ValidateRegistryOwnershipAsync(
            moduleRoot,
            cancellationToken);
        if (registryFailure is not null)
        {
            return Failure(registryFailure);
        }

        var originalContent = await ReadStrictTextAsync(
            moduleEntryFullPath,
            cancellationToken);
        var edit = ModuleEntryIntegrationEditor.Edit(
            originalContent,
            schema.RootNamespace);
        if (!edit.Succeeded)
        {
            return new ModuleEntryIntegrationApplyResult(
                applied: false,
                changed: false,
                compilation: null,
                edit.Diagnostics);
        }

        if (!edit.Changed)
        {
            return new ModuleEntryIntegrationApplyResult(
                applied: true,
                changed: false,
                compilation: null,
                diagnostics: []);
        }

        var compilation =
            await ModuleIntegrationCompilationCommand.ValidateEntryAsync(
                root,
                schema,
                target,
                moduleEntryFullPath,
                edit.DesiredContent,
                cancellationToken);
        if (!compilation.Succeeded)
        {
            return new ModuleEntryIntegrationApplyResult(
                applied: false,
                changed: false,
                compilation,
                diagnostics: []);
        }

        await ApplyUnderWorkspaceLockAsync(
            moduleRoot,
            moduleEntryFullPath,
            originalContent,
            edit.DesiredContent,
            cancellationToken);
        return new ModuleEntryIntegrationApplyResult(
            applied: true,
            changed: true,
            compilation,
            diagnostics: []);
    }

    private static async Task ApplyUnderWorkspaceLockAsync(
        string moduleRoot,
        string moduleEntryFullPath,
        string originalContent,
        string desiredContent,
        CancellationToken cancellationToken)
    {
        var lockPath = Path.Combine(
            moduleRoot,
            WorkspaceLockRelativePath.Replace(
                '/',
                Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        await using var workspaceLock = OpenWorkspaceLock(lockPath);

        var registryFailure = await ValidateRegistryOwnershipAsync(
            moduleRoot,
            cancellationToken);
        if (registryFailure is not null)
        {
            throw new GenerationWorkspaceConflictException(
                registryFailure,
                ModuleIntegrationBackendWorkspace.RegistryRelativePath);
        }

        var currentContent = await ReadStrictTextAsync(
            moduleEntryFullPath,
            cancellationToken);
        if (!StringComparer.Ordinal.Equals(
                currentContent,
                originalContent))
        {
            throw new GenerationWorkspaceConflictException(
                "候选编译后模块入口发生并发变化，拒绝覆盖。",
                Path.GetFileName(moduleEntryFullPath));
        }

        cancellationToken.ThrowIfCancellationRequested();
        await ReplaceTextAsync(
            moduleEntryFullPath,
            desiredContent,
            cancellationToken);
    }

    /// <summary>
    /// 通过捕获工作区快照并对比聚合桥清单中的 SHA256，验证模块聚合桥仍由生成清单拥有；任何漂移或缺失均返回中文诊断字符串而非抛异常。
    /// </summary>
    /// <remarks>
    /// 该方法是 Composition 与 ClientRoute 接入命令的前置门禁：只有聚合桥仍由生成清单拥有时，才允许修改手写模块入口、Composition 或客户端路由。
    /// 失败原因通常是用户在 apply-module-integration 之后手动编辑了聚合桥，或并发执行了 apply-module-integration；调用方应将返回的诊断直接反馈给 CLI 使用者。
    /// </remarks>
    /// <param name="moduleRoot">模块项目所在目录的绝对路径。</param>
    /// <param name="cancellationToken">用于取消快照捕获的令牌。</param>
    /// <returns>失败诊断字符串；返回 null 表示聚合桥仍由生成清单拥有，可继续修改手写文件。</returns>
    internal static async Task<string?> ValidateRegistryOwnershipAsync(
        string moduleRoot,
        CancellationToken cancellationToken)
    {
        var snapshot = await GenerationWorkspaceStore.CaptureAsync(
            moduleRoot,
            [
                new GeneratedArtifact(
                    ModuleIntegrationBackendWorkspace.RegistryRelativePath,
                    GeneratedArtifactKind.Backend,
                    "\n"),
            ],
            cancellationToken);
        if (snapshot.PreviousManifest is null
            || !snapshot.PreviousManifest.TryGetSha256(
                ModuleIntegrationBackendWorkspace.RegistryRelativePath,
                out var expectedSha256)
            || expectedSha256 is null)
        {
            return "模块聚合桥尚未由生成清单拥有，请先执行 apply-module-integration。";
        }

        if (!snapshot.ExistingFiles.TryGetValue(
                ModuleIntegrationBackendWorkspace.RegistryRelativePath,
                out var registryContent)
            || !StringComparer.Ordinal.Equals(
                ComputeHash(registryContent),
                expectedSha256))
        {
            return "模块聚合桥缺失或已漂移，拒绝修改手写入口。";
        }

        return null;
    }

    private static string ComputeHash(string content) =>
        Convert.ToHexString(
                SHA256.HashData(StrictUtf8.GetBytes(content)))
            .ToLowerInvariant();

    private static FileStream OpenWorkspaceLock(string lockPath)
    {
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
                WorkspaceLockRelativePath,
                exception);
        }
    }

    private static async Task ReplaceTextAsync(
        string targetPath,
        string content,
        CancellationToken cancellationToken)
    {
        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(targetPath)!,
            $".fullnet-module-entry-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             new FileStreamOptions
                             {
                                 Mode = FileMode.CreateNew,
                                 Access = FileAccess.Write,
                                 Share = FileShare.None,
                                 Options = FileOptions.Asynchronous
                                     | FileOptions.WriteThrough,
                             }))
            {
                var bytes = StrictUtf8.GetBytes(content);
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(
                temporaryPath,
                targetPath,
                overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
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
                "模块入口不得包含 UTF-8 BOM。");
        }

        return StrictUtf8.GetString(bytes);
    }

    private static string Resolve(
        string repositoryRoot,
        string relativePath) =>
        Path.GetFullPath(Path.Combine(
            repositoryRoot,
            relativePath.Replace(
                '/',
                Path.DirectorySeparatorChar)));

    private static bool IsWithin(
        string expectedParent,
        string path)
    {
        var relative = Path.GetRelativePath(expectedParent, path);
        return !StringComparer.Ordinal.Equals(relative, "..")
            && !relative.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal)
            && !Path.IsPathFullyQualified(relative);
    }

    private static bool MatchesModule(
        string rootNamespace,
        string moduleName) =>
        StringComparer.Ordinal.Equals(rootNamespace, moduleName)
        || rootNamespace.EndsWith(
            $".{moduleName}",
            StringComparison.Ordinal);

    private static ModuleEntryIntegrationApplyResult Failure(
        string diagnostic) =>
        new(
            applied: false,
            changed: false,
            ModuleIntegrationCompilationResult.Failure([diagnostic]),
            diagnostics: []);
}
