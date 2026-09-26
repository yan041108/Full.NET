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
            var contributorFullPath = Path.Combine(
                Path.GetFullPath(repositoryRoot),
                target.AuthorizationContributorPath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
            if (!File.Exists(contributorFullPath))
            {
                return ModuleIntegrationHostApplyResult.Failure(
                    "显式 AuthorizationContributor 文件不存在。");
            }

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
                await File.WriteAllTextAsync(
                        contributorFullPath,
                        edited.DesiredContent,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return ModuleIntegrationHostApplyResult.Success();
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

        // 复用排他锁、提交前快照复核与清单最后提交；现有工作区仍有逐文件提交中途失败的恢复缺口。
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
