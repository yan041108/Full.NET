using System.Collections.ObjectModel;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.Data.CodeGeneration.Integration;

/// <summary>
/// 保存模块后端写盘的计划、编译门禁与最终提交状态。
/// </summary>
public sealed class ModuleIntegrationBackendApplyResult
{
    internal ModuleIntegrationBackendApplyResult(
        IEnumerable<GenerationWriteAction> actions,
        ModuleIntegrationCompilationResult? compilation,
        bool applied)
    {
        Actions = new ReadOnlyCollection<GenerationWriteAction>(
            actions.ToArray());
        Compilation = compilation;
        Applied = applied;
    }

    public IReadOnlyList<GenerationWriteAction> Actions { get; }

    public ModuleIntegrationCompilationResult? Compilation { get; }

    public bool Applied { get; }
}

/// <summary>
/// 先规划和编译当前实体后端，再复用工作区存储器完成并发安全的原子写盘。
/// </summary>
public static class ModuleIntegrationBackendApplyCommand
{
    public static async Task<ModuleIntegrationBackendApplyResult> ApplyAsync(
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

        var moduleProjectFullPath = Path.Combine(
            root,
            target.ModuleProjectPath.Replace(
                '/',
                Path.DirectorySeparatorChar));
        if (!File.Exists(moduleProjectFullPath))
        {
            return Failure(
                "模块项目不存在，无法执行编译验证。");
        }

        var moduleRoot = Path.GetDirectoryName(moduleProjectFullPath)
            ?? throw new InvalidOperationException(
                "模块项目缺少父目录。");
        var plan = await ModuleIntegrationBackendWorkspace.PlanAsync(
            moduleRoot,
            schema,
            cancellationToken);
        var entityPrefix = $"Generated/{schema.ClrTypeName}/";
        var actions = plan.Actions
            .Where(action =>
                action.RelativePath.StartsWith(
                    entityPrefix,
                    StringComparison.Ordinal)
                || StringComparer.Ordinal.Equals(
                    action.RelativePath,
                    ModuleIntegrationBackendWorkspace.RegistryRelativePath))
            .ToArray();
        if (!plan.CanApply)
        {
            return new ModuleIntegrationBackendApplyResult(
                actions,
                compilation: null,
                applied: false);
        }

        var sourcePathsToRemove = actions
            .Select(action => Path.Combine(
                moduleRoot,
                action.RelativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar)))
            .ToArray();
        var candidateArtifacts = actions
            .Where(action => action.Content is not null)
            .Select(action => new GeneratedArtifact(
                action.RelativePath,
                GeneratedArtifactKind.Backend,
                action.Content!))
            .ToArray();
        var compilation =
            await ModuleIntegrationCompilationCommand.ValidateAsync(
                root,
                schema,
                target,
                sourcePathsToRemove,
                candidateArtifacts,
                cancellationToken);
        if (!compilation.Succeeded)
        {
            return new ModuleIntegrationBackendApplyResult(
                actions: [],
                compilation,
                applied: false);
        }

        await GenerationWorkspaceStore.ApplyAsync(
            moduleRoot,
            plan,
            cancellationToken);
        return new ModuleIntegrationBackendApplyResult(
            actions,
            compilation,
            applied: true);
    }

    private static bool MatchesModule(
        string rootNamespace,
        string moduleName) =>
        StringComparer.Ordinal.Equals(rootNamespace, moduleName)
        || rootNamespace.EndsWith(
            $".{moduleName}",
            StringComparison.Ordinal);

    private static ModuleIntegrationBackendApplyResult Failure(
        string diagnostic) =>
        new(
            actions: [],
            ModuleIntegrationCompilationResult.Failure([diagnostic]),
            applied: false);
}
