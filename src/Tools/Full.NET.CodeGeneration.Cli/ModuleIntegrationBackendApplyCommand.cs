using System.Collections.ObjectModel;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.CodeGeneration.Integration;
using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.CodeGeneration.Cli;

/// <summary>
/// 保存模块后端写盘的计划、编译门禁与最终提交状态。
/// </summary>
internal sealed class ModuleIntegrationBackendApplyResult
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
internal static class ModuleIntegrationBackendApplyCommand
{
    /// <summary>
    /// 先规划并候选编译当前实体后端生成产物，再复用工作区存储器完成并发安全的原子写盘；候选编译失败或前置校验失败均返回未提交结果。
    /// </summary>
    /// <remarks>
    /// 候选编译通过 ModuleIntegrationBuildProjection 在系统临时目录构建，不修改仓库；只有编译成功才允许写盘。
    /// 写盘前过滤掉非本实体的动作，避免误删其他实体的生成产物。
    /// </remarks>
    /// <param name="repositoryRoot">仓库根目录，用于解析模块项目路径。</param>
    /// <param name="schema">CRUD Schema，提供根命名空间、CLR 类型名与生成产物。</param>
    /// <param name="target">模块接入目标，提供模块项目相对路径。</param>
    /// <param name="cancellationToken">用于取消文件 IO 与编译进程的令牌。</param>
    /// <returns>提交结果，包含已执行动作、候选编译诊断与是否已应用。</returns>
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
