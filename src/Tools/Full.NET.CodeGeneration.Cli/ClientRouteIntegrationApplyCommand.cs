using System.Collections.ObjectModel;
using System.Text;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.CodeGeneration.Integration;
using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.CodeGeneration.Cli;

/// <summary>
/// 保存双管理端路由接入的写盘状态与保守诊断。
/// </summary>
internal sealed class ClientRouteIntegrationApplyResult
{
    internal ClientRouteIntegrationApplyResult(
        bool applied,
        bool vueChanged,
        bool layuiChanged,
        IEnumerable<string> diagnostics)
    {
        Applied = applied;
        VueChanged = vueChanged;
        LayuiChanged = layuiChanged;
        Diagnostics = new ReadOnlyCollection<string>(
            diagnostics.ToArray());
    }

    public bool Applied { get; }

    public bool VueChanged { get; }

    public bool LayuiChanged { get; }

    public IReadOnlyList<string> Diagnostics { get; }
}

/// <summary>
/// 在全部后端接线和显式本地适配文件通过复核后提交双管理端路由。
/// </summary>
internal static class ClientRouteIntegrationApplyCommand
{
    private const string ModuleWorkspaceLockRelativePath =
        ".fullnet/codegeneration.lock";
    private const string CompositionLockRelativePath =
        ".fullnet/codegeneration-composition.lock";
    private const string ClientRouteLockRelativePath =
        ".fullnet/codegeneration-client-routes.lock";

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    /// <summary>
    /// 在所有后端接线（模块入口、Composition）稳定且本地适配文件通过复核后，原子提交 Vue 与 Layui 双管理端路由；任一前置失败均返回未提交结果。
    /// </summary>
    /// <remarks>
    /// 三锁互斥：Composition 仓库锁 + 模块工作区锁 + 客户端路由专用锁，确保跨进程不会同时修改双管理端路由。
    /// 提交顺序：先 stage 候选 Layui 路由与原 Vue 路由恢复副本，再原子 Move 候选 Vue 路由，再 Move 候选 Layui 路由；任何中间失败均尝试用恢复副本回滚 Vue 路由，回滚失败则抛出包含恢复副本路径的异常要求人工审查。
    /// Vue 路由文件不能为空，Layui controller 必须包含目标 export function，否则拒绝猜测创建。
    /// </remarks>
    /// <param name="repositoryRoot">仓库根目录，用于解析 Vue 与 Layui 路由及适配文件路径。</param>
    /// <param name="schema">CRUD Schema，提供根命名空间与模块名匹配。</param>
    /// <param name="target">模块接入目标，必须包含 clientRoute 子目标。</param>
    /// <param name="cancellationToken">用于取消文件 IO 与锁操作的令牌。</param>
    /// <returns>提交结果，包含是否已应用、Vue 是否变更、Layui 是否变更与失败诊断。</returns>
    public static async Task<ClientRouteIntegrationApplyResult> ApplyAsync(
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

        var route = target.ClientRoute;
        if (route is null)
        {
            return Failure(
                "模块接入目标 JSON 缺少显式 clientRoute，拒绝猜测客户端可执行路径。");
        }

        if (!MatchesModule(schema.RootNamespace, target.ModuleName))
        {
            return Failure(
                "Schema 根命名空间与显式目标模块不匹配。");
        }

        var moduleProject = Resolve(root, target.ModuleProjectPath);
        var moduleEntry = Resolve(root, target.ModuleEntryPointPath);
        var compositionProject = Resolve(
            root,
            target.CompositionProjectPath);
        var compositionCatalog = Resolve(
            root,
            target.CompositionCatalogPath);
        var vueRouter = Resolve(root, target.VueRouterPath);
        var layuiRouter = Resolve(root, target.LayuiRouterPath!);
        var vueComponent = Resolve(
            root,
            route.VueComponentPath);
        var layuiController = Resolve(
            root,
            route.LayuiControllerPath!);
        var requiredFiles = new[]
        {
            moduleProject,
            moduleEntry,
            compositionProject,
            compositionCatalog,
            vueRouter,
            layuiRouter,
            vueComponent,
            layuiController,
        };
        if (requiredFiles.Any(path => !File.Exists(path)))
        {
            return Failure(
                "客户端路由接入所需的模块、Composition、路由或适配文件不存在。");
        }

        var moduleRoot = Path.GetDirectoryName(moduleProject)
            ?? throw new InvalidOperationException(
                "模块项目缺少父目录。");
        var registryFailure =
            await ModuleEntryIntegrationApplyCommand
                .ValidateRegistryOwnershipAsync(
                    moduleRoot,
                    cancellationToken);
        if (registryFailure is not null)
        {
            return Failure(registryFailure);
        }

        var originals = new Dictionary<string, string>(
            StringComparer.Ordinal)
        {
            [moduleEntry] = await ReadStrictTextAsync(
                moduleEntry,
                cancellationToken),
            [compositionProject] = await ReadStrictTextAsync(
                compositionProject,
                cancellationToken),
            [compositionCatalog] = await ReadStrictTextAsync(
                compositionCatalog,
                cancellationToken),
            [vueRouter] = await ReadStrictTextAsync(
                vueRouter,
                cancellationToken),
            [layuiRouter] = await ReadStrictTextAsync(
                layuiRouter,
                cancellationToken),
            [vueComponent] = await ReadStrictTextAsync(
                vueComponent,
                cancellationToken),
            [layuiController] = await ReadStrictTextAsync(
                layuiController,
                cancellationToken),
        };
        var prerequisiteFailure = ValidatePrerequisites(
            schema,
            target,
            originals[moduleEntry],
            originals[compositionProject],
            originals[compositionCatalog]);
        if (prerequisiteFailure is not null)
        {
            return Failure(prerequisiteFailure);
        }

        if (string.IsNullOrWhiteSpace(originals[vueComponent]))
        {
            return Failure(
                "显式 Vue View 为空，无法建立本地可信路由映射。");
        }

        if (!ContainsLayuiExport(
                originals[layuiController],
                route.LayuiControllerExport!))
        {
            return Failure(
                "显式 Layui controller 文件不包含目标 export function。");
        }

        var vueEdit = VueRouteIntegrationEditor.Edit(
            originals[vueRouter],
            target.VueRouterPath,
            route);
        var layuiEdit = LayuiRouteIntegrationEditor.Edit(
            originals[layuiRouter],
            target.LayuiRouterPath!,
            route);
        if (!vueEdit.Succeeded || !layuiEdit.Succeeded)
        {
            return new ClientRouteIntegrationApplyResult(
                applied: false,
                vueChanged: false,
                layuiChanged: false,
                vueEdit.Diagnostics.Concat(
                    layuiEdit.Diagnostics));
        }

        if (!vueEdit.Changed && !layuiEdit.Changed)
        {
            return new ClientRouteIntegrationApplyResult(
                applied: true,
                vueChanged: false,
                layuiChanged: false,
                diagnostics: []);
        }

        await CommitAsync(
            root,
            moduleRoot,
            originals,
            vueRouter,
            vueEdit,
            layuiRouter,
            layuiEdit,
            cancellationToken);
        return new ClientRouteIntegrationApplyResult(
            applied: true,
            vueEdit.Changed,
            layuiEdit.Changed,
            diagnostics: []);
    }

    private static string? ValidatePrerequisites(
        FullNetCrudSchema schema,
        ModuleIntegrationTarget target,
        string moduleEntry,
        string compositionProject,
        string compositionCatalog)
    {
        var entryEdit = ModuleEntryIntegrationEditor.Edit(
            moduleEntry,
            schema.RootNamespace);
        if (!entryEdit.Succeeded || entryEdit.Changed)
        {
            return "模块入口尚未完成稳定聚合调用，请先执行 apply-module-entry-integration。";
        }

        var projectEdit = CompositionProjectEditor.Edit(
            compositionProject,
            target.CompositionProjectPath,
            target.ModuleProjectPath);
        var catalogEdit = CompositionCatalogEditor.Edit(
            compositionCatalog,
            schema.RootNamespace,
            target.ModuleName);
        if (!projectEdit.Succeeded
            || projectEdit.Changed
            || !catalogEdit.Succeeded
            || catalogEdit.Changed)
        {
            return "Composition 尚未完成精确接入，请先执行 apply-composition-integration。";
        }

        return null;
    }

    private static bool ContainsLayuiExport(
        string source,
        string exportName)
    {
        if (source.Contains('`'))
        {
            return false;
        }

        var lines = ClientRouteText.SplitLines(source);
        var sanitized = ClientRouteText.RemoveComments(lines);
        return sanitized.Any(line =>
            line.TrimStart().StartsWith(
                $"export function {exportName}(",
                StringComparison.Ordinal));
    }

    private static async Task CommitAsync(
        string repositoryRoot,
        string moduleRoot,
        IReadOnlyDictionary<string, string> originals,
        string vueRouterPath,
        ClientRouteIntegrationEditResult vueEdit,
        string layuiRouterPath,
        ClientRouteIntegrationEditResult layuiEdit,
        CancellationToken cancellationToken)
    {
        var compositionLockPath = Resolve(
            repositoryRoot,
            CompositionLockRelativePath);
        var moduleLockPath = Resolve(
            moduleRoot,
            ModuleWorkspaceLockRelativePath);
        var clientLockPath = Resolve(
            repositoryRoot,
            ClientRouteLockRelativePath);
        Directory.CreateDirectory(
            Path.GetDirectoryName(compositionLockPath)!);
        Directory.CreateDirectory(
            Path.GetDirectoryName(moduleLockPath)!);
        Directory.CreateDirectory(
            Path.GetDirectoryName(clientLockPath)!);
        await using var compositionLock = OpenLock(
            compositionLockPath,
            CompositionLockRelativePath);
        await using var moduleLock = OpenLock(
            moduleLockPath,
            ModuleWorkspaceLockRelativePath);
        await using var clientLock = OpenLock(
            clientLockPath,
            ClientRouteLockRelativePath);

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

        foreach (var original in originals)
        {
            await EnsureUnchangedAsync(
                original.Key,
                original.Value,
                cancellationToken);
        }

        string? stagedVue = null;
        string? vueRecovery = null;
        string? stagedLayui = null;
        var vueCommitted = false;
        try
        {
            stagedVue = vueEdit.Changed
                ? await StageTextAsync(
                    vueRouterPath,
                    vueEdit.DesiredContent,
                    cancellationToken)
                : null;
            vueRecovery = vueEdit.Changed
                ? await StageTextAsync(
                    vueRouterPath,
                    originals[vueRouterPath],
                    cancellationToken)
                : null;
            stagedLayui = layuiEdit.Changed
                ? await StageTextAsync(
                    layuiRouterPath,
                    layuiEdit.DesiredContent,
                    cancellationToken)
                : null;
            cancellationToken.ThrowIfCancellationRequested();
            if (stagedVue is not null)
            {
                File.Move(
                    stagedVue,
                    vueRouterPath,
                    overwrite: true);
                stagedVue = null;
                vueCommitted = true;
            }

            if (stagedLayui is not null)
            {
                File.Move(
                    stagedLayui,
                    layuiRouterPath,
                    overwrite: true);
                stagedLayui = null;
            }
        }
        catch (Exception commitException)
        {
            if (vueCommitted
                && vueRecovery is not null
                && File.Exists(vueRecovery))
            {
                try
                {
                    File.Move(
                        vueRecovery,
                        vueRouterPath,
                        overwrite: true);
                    vueRecovery = null;
                }
                catch (Exception recoveryException)
                    when (recoveryException is IOException
                        or UnauthorizedAccessException)
                {
                    var preservedRecovery = vueRecovery;
                    vueRecovery = null;
                    throw new GenerationWorkspaceConflictException(
                        "Layui 路由提交失败且 Vue 路由回滚失败；"
                        + "原 Vue 路由恢复副本已保留，必须人工审查。",
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
            DeleteIfExists(stagedVue);
            DeleteIfExists(stagedLayui);
            DeleteIfExists(vueRecovery);
        }
    }

    private static async Task EnsureUnchangedAsync(
        string path,
        string expected,
        CancellationToken cancellationToken)
    {
        var current = await ReadStrictTextAsync(path, cancellationToken);
        if (!StringComparer.Ordinal.Equals(current, expected))
        {
            throw new GenerationWorkspaceConflictException(
                "客户端路由候选验证后相关源文件发生变化。",
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
                "另一生成写盘进程正在占用客户端接入锁。",
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
            $".fullnet-client-route-{Guid.NewGuid():N}.tmp");
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
                "客户端路由接入文本不得包含 UTF-8 BOM。");
        }

        return StrictUtf8.GetString(bytes);
    }

    private static string Resolve(
        string root,
        string relativePath) =>
        Path.GetFullPath(Path.Combine(
            root,
            relativePath.Replace(
                '/',
                Path.DirectorySeparatorChar)));

    private static void DeleteIfExists(string? path)
    {
        if (path is not null && File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static bool MatchesModule(
        string rootNamespace,
        string moduleName) =>
        StringComparer.Ordinal.Equals(rootNamespace, moduleName)
        || rootNamespace.EndsWith(
            $".{moduleName}",
            StringComparison.Ordinal);

    private static ClientRouteIntegrationApplyResult Failure(
        string diagnostic) =>
        new(
            applied: false,
            vueChanged: false,
            layuiChanged: false,
            [diagnostic]);
}
