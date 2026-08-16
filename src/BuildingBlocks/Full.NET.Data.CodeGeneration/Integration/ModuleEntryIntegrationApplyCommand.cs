using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.Data.CodeGeneration.Integration;

/// <summary>
/// 保存模块入口接线的源码状态、编译门禁和提交结果。
/// </summary>
public sealed class ModuleEntryIntegrationApplyResult
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
public static class ModuleEntryIntegrationApplyCommand
{
    private const string WorkspaceLockRelativePath =
        ".fullnet/codegeneration.lock";

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

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
