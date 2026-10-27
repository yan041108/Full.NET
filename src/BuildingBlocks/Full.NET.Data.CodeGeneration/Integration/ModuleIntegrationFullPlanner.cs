using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.Data.CodeGeneration.Integration;

/// <summary>在内存中合并整链接入候选，所有目标均在第一次业务写入前解析。</summary>
public static class ModuleIntegrationFullPlanner
{
    /// <summary>把模块、组合、页面和授权的全部变化合并到同一份工作区计划。</summary>
    public static async Task<GenerationWritePlan> PlanAsync(string repositoryRoot, FullNetCrudSchema schema,
        ModuleIntegrationTarget target, GenerationWritePlan artifactsPlan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifactsPlan);
        if (!artifactsPlan.CanApply) return artifactsPlan;
        var root = GenerationWorkspacePath.NormalizeRoot(repositoryRoot);
        if (target.ModuleProjectPath.Contains("Full.NET.Modules.", StringComparison.Ordinal)
            || !(schema.RootNamespace == target.ModuleName || schema.RootNamespace.EndsWith("." + target.ModuleName, StringComparison.Ordinal)))
            throw new ArgumentException("仅允许命名空间匹配的显式业务模块接入。");
        string Resolve(string path) => GenerationWorkspacePath.ResolveFile(root, path);
        // 全部路径先完成包含关系、链接与别名检查，避免迟到的非法目标被较早的缺失文件遮蔽。
        foreach (var path in new[] { target.ModuleProjectPath, target.ModuleEntryPointPath,
                     target.CompositionProjectPath, target.CompositionCatalogPath, target.AuthorizationContributorPath,
                     target.ClientRoute is null ? null : target.VueRouterPath, target.ClientRoute?.VueComponentPath })
            if (path is not null) _ = Resolve(path);
        var moduleProject = Resolve(target.ModuleProjectPath);
        var moduleRoot = Path.GetDirectoryName(moduleProject)!;
        var modulePrefix = Path.GetRelativePath(root, moduleRoot).Replace('\\', '/') + "/";
        if (!target.ModuleEntryPointPath.StartsWith(modulePrefix, StringComparison.Ordinal)
            || (target.AuthorizationContributorPath is { } authorization && !authorization.StartsWith(modulePrefix, StringComparison.Ordinal)))
            throw new ArgumentException("模块入口与授权贡献者必须属于显式模块项目目录。");
        _ = await File.ReadAllTextAsync(moduleProject, cancellationToken);
        CompositionIntegrationRecovery.RejectPending(root, Resolve(target.CompositionProjectPath), Resolve(target.CompositionCatalogPath));

        var desired = new Dictionary<string, (string Content, bool Shared)>(StringComparer.Ordinal);
        var originals = new Dictionary<string, string>(StringComparer.Ordinal);
        async Task<string> Read(string path)
        {
            var text = await File.ReadAllTextAsync(Resolve(path), cancellationToken);
            originals.Add(path, text);
            return text;
        }
        void Set(string path, string content, bool shared)
        {
            _ = Resolve(path);
            if (!desired.TryAdd(path, (content, shared))) throw new ArgumentException("接入目标存在重复或互相覆盖的文件路径。");
        }
        static string Edited(bool success, string text, IReadOnlyList<string> diagnostics) => success
            ? text : throw new ArgumentException(string.Join("\n", diagnostics));

        foreach (var artifact in ModuleIntegrationBackendWorkspace.CreateArtifacts(schema))
            Set(modulePrefix + artifact.RelativePath, artifact.Content, shared: false);
        var entityNames = artifactsPlan.PreviousManifest?.Artifacts
            .Where(entry => entry.RelativePath.StartsWith(modulePrefix + "Generated/", StringComparison.Ordinal)
                && entry.RelativePath.EndsWith("Feature.g.cs", StringComparison.Ordinal))
            .Select(entry => Path.GetFileName(entry.RelativePath)[..^"Feature.g.cs".Length]) ?? [];
        var registry = ModuleIntegrationBackendWorkspace.CreateRegistryArtifact(schema, entityNames.Append(schema.ClrTypeName));
        Set(modulePrefix + registry.RelativePath, registry.Content, shared: true);
        var entry = ModuleEntryIntegrationEditor.Edit(await Read(target.ModuleEntryPointPath), schema.RootNamespace);
        Set(target.ModuleEntryPointPath, Edited(entry.Succeeded, entry.DesiredContent, entry.Diagnostics), shared: true);
        var project = CompositionProjectEditor.Edit(await Read(target.CompositionProjectPath), Resolve(target.CompositionProjectPath), moduleProject);
        Set(target.CompositionProjectPath, Edited(project.Succeeded, project.DesiredContent, project.Diagnostics), shared: true);
        var catalog = CompositionCatalogEditor.Edit(await Read(target.CompositionCatalogPath), schema.RootNamespace, target.ModuleName);
        Set(target.CompositionCatalogPath, Edited(catalog.Succeeded, catalog.DesiredContent, catalog.Diagnostics), shared: true);
        if (target.ClientRoute is { } route)
        {
            if (target.LayuiRouterPath is not null || route.LayuiControllerPath is not null)
                throw new ArgumentException("Host 整链接入仅维护 Vue；Layui 冻结路径须单独明确授权。");
            var directory = route.VueComponentPath.Contains('/')
                ? route.VueComponentPath[..(route.VueComponentPath.LastIndexOf('/') + 1)] : string.Empty;
            foreach (var artifact in CrudArtifactGenerator.Generate(schema).Where(artifact =>
                         artifact.Kind is GeneratedArtifactKind.VueView or GeneratedArtifactKind.VueClient))
                Set(artifact.Kind == GeneratedArtifactKind.VueView ? route.VueComponentPath
                    : directory + Path.GetFileName(artifact.RelativePath), artifact.Content, shared: false);
            var router = VueRouteIntegrationEditor.Edit(await Read(target.VueRouterPath), target.VueRouterPath, route);
            Set(target.VueRouterPath, Edited(router.Succeeded, router.DesiredContent, router.Diagnostics), shared: true);
        }
        if (target.AuthorizationContributorPath is { } contributor)
        {
            ModuleIntegrationHostOrchestrator.RejectAuthorizationPending(root, contributor);
            var edit = AuthorizationContributorIntegrationEditor.Edit(await Read(contributor), contributor,
                CrudAuthorizationContributorFragmentGenerator.Generate(schema));
            Set(contributor, Edited(edit.Succeeded, edit.DesiredContent, edit.Diagnostics), shared: true);
        }

        var candidates = desired.Select(item => new GeneratedArtifact(item.Key, GeneratedArtifactKind.Backend, item.Value.Content)).ToArray();
        var snapshot = await GenerationWorkspaceStore.CaptureAsync(root, candidates, cancellationToken);
        if (snapshot.PreviousManifest?.ToJson() != artifactsPlan.PreviousManifest?.ToJson())
            throw new GenerationWorkspaceConflictException("接入规划期间工作区清单已变化。", GenerationWorkspaceStore.ManifestRelativePath);
        foreach (var original in originals)
            if (!snapshot.ExistingFiles.TryGetValue(original.Key, out var actual) || actual != original.Value)
                throw new GenerationWorkspaceConflictException("接入规划期间手写文件已变化。", original.Key);

        var actions = artifactsPlan.Actions.ToDictionary(action => action.RelativePath, StringComparer.Ordinal);
        var entries = artifactsPlan.NextManifest!.Artifacts.ToDictionary(item => item.RelativePath, StringComparer.Ordinal);
        // 基础规划把未列入本次产物的旧文件视为删除；整链接入保留其它实体及共享入口。
        foreach (var previous in snapshot.PreviousManifest?.Artifacts ?? [])
        {
            if (!actions.TryGetValue(previous.RelativePath, out var action) || action.Kind != GenerationWriteActionKind.Delete
                || desired.ContainsKey(previous.RelativePath)) continue;
            var existing = snapshot.ExistingFiles.GetValueOrDefault(previous.RelativePath);
            if (existing is null || GenerationContentHash.Compute(existing) != previous.Sha256)
                throw new GenerationWorkspaceConflictException("其它受管文件已漂移，禁止覆盖。", previous.RelativePath);
            actions[previous.RelativePath] = new(previous.RelativePath, GenerationWriteActionKind.Unchanged, existing, previous.Sha256, previous.Sha256);
            entries[previous.RelativePath] = previous;
        }
        foreach (var candidate in desired)
        {
            if (actions.TryGetValue(candidate.Key, out var priorAction) && priorAction.Kind != GenerationWriteActionKind.Delete)
                throw new ArgumentException("接入目标不得覆盖基础生成产物。");
            var existing = snapshot.ExistingFiles.GetValueOrDefault(candidate.Key);
            var oldHash = existing is null ? null : GenerationContentHash.Compute(existing);
            var newHash = GenerationContentHash.Compute(candidate.Value.Content);
            var previous = snapshot.PreviousManifest?.Artifacts.SingleOrDefault(item => item.RelativePath == candidate.Key);
            if (previous is not null && (previous.Sha256 != oldHash))
                throw new GenerationWorkspaceConflictException("接入目标已发生人工漂移或属于另一实体。", candidate.Key);
            var mayEdit = originals.ContainsKey(candidate.Key) || (previous is not null && previous.Sha256 == oldHash);
            var kind = existing is null ? GenerationWriteActionKind.Create : oldHash == newHash
                ? GenerationWriteActionKind.Unchanged : mayEdit ? GenerationWriteActionKind.Update : GenerationWriteActionKind.Conflict;
            actions[candidate.Key] = new(candidate.Key, kind, candidate.Value.Content, oldHash, newHash);
            entries[candidate.Key] = new(candidate.Key, newHash);
        }
        var ordered = actions.Values.OrderBy(action => action.RelativePath, StringComparer.Ordinal).ToArray();
        return new GenerationWritePlan(ordered, artifactsPlan.PreviousManifest,
            ordered.Any(action => action.Kind == GenerationWriteActionKind.Conflict) ? null : GenerationManifest.Create(entries.Values));
    }
}
