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
            await WriteVueViewAsync(
                    repositoryRoot,
                    schema,
                    target.ClientRoute,
                    cancellationToken)
                .ConfigureAwait(false);
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
        var vueView = CrudArtifactGenerator.Generate(schema)
            .Single(artifact => artifact.Kind == GeneratedArtifactKind.VueView);
        var destination = Path.Combine(
            Path.GetFullPath(repositoryRoot),
            route.VueComponentPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var pageModel = CrudArtifactGenerator.Generate(schema)
            .Single(artifact =>
                artifact.RelativePath.EndsWith(
                    "-page.generated.ts",
                    StringComparison.Ordinal));
        var client = CrudArtifactGenerator.Generate(schema)
            .Single(artifact =>
                artifact.Kind == GeneratedArtifactKind.VueClient
                && artifact.RelativePath.EndsWith(
                    ".generated.ts",
                    StringComparison.Ordinal)
                && !artifact.RelativePath.EndsWith(
                    "-page.generated.ts",
                    StringComparison.Ordinal));
        var directory = Path.GetDirectoryName(destination)!;
        await File.WriteAllTextAsync(
                destination,
                vueView.Content,
                cancellationToken)
            .ConfigureAwait(false);
        await File.WriteAllTextAsync(
                Path.Combine(directory, Path.GetFileName(pageModel.RelativePath)),
                pageModel.Content,
                cancellationToken)
            .ConfigureAwait(false);
        await File.WriteAllTextAsync(
                Path.Combine(directory, Path.GetFileName(client.RelativePath)),
                client.Content,
                cancellationToken)
            .ConfigureAwait(false);
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
