using System.Diagnostics;
using System.Text;
using System.Xml.Linq;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.Data.CodeGeneration.Integration;

/// <summary>在只包含候选文件的临时投影中编译完整接入计划，不复制仓库或改写真实源码。</summary>
public static class ModuleIntegrationFullCompilation
{
    /// <summary>同时注入模块与 Composition 候选，编译成功后才允许提交工作区。</summary>
    public static async Task<ModuleIntegrationCompilationResult> ValidateAsync(string repositoryRoot,
        FullNetCrudSchema schema, ModuleIntegrationTarget target, GenerationWritePlan plan,
        CancellationToken cancellationToken = default)
    {
        if (!plan.CanApply) return ModuleIntegrationCompilationResult.Failure(["整链接入计划存在冲突。"]);
        var root = GenerationWorkspacePath.NormalizeRoot(repositoryRoot);
        var temporary = Path.Combine(Path.GetTempPath(), "fullnet-codegen-full-build-" + Guid.NewGuid().ToString("N"));
        try
        {
            var sdk = await RunAsync(root, ["--list-sdks"], cancellationToken);
            if (sdk.Code != 0 || !sdk.Output.Split('\n').Any(line => line.StartsWith("10.", StringComparison.Ordinal)))
                return ModuleIntegrationCompilationResult.Failure(["整链接入编译要求 .NET 10 SDK。"]);
            var moduleProject = GenerationWorkspacePath.ResolveFile(root, target.ModuleProjectPath);
            var moduleRoot = Path.GetDirectoryName(moduleProject)!;
            var prefix = Path.GetRelativePath(root, moduleRoot).Replace('\\', '/') + "/";
            var moduleActions = plan.Actions.Where(action => action.RelativePath.StartsWith(prefix, StringComparison.Ordinal)
                && action.RelativePath.EndsWith(".cs", StringComparison.Ordinal)).ToArray();
            var candidates = moduleActions.Where(action => action.Content is not null)
                .Select(action => new GeneratedArtifact(action.RelativePath, GeneratedArtifactKind.Backend, action.Content!)).ToArray();
            if (candidates.Select(item => Path.GetFileName(item.RelativePath)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != candidates.Length)
                return ModuleIntegrationCompilationResult.Failure(["编译候选中存在同名 C# 文件，不能投影为不明确的输入。"]);
            var module = ModuleIntegrationBuildProjection.Create(schema, moduleProject, Path.Combine(temporary, "module"),
                moduleActions.Select(action => GenerationWorkspacePath.ResolveFile(root, action.RelativePath)).ToArray(), candidates);
            var catalog = plan.Actions.Single(action => action.RelativePath == target.CompositionCatalogPath).Content!;
            var projectChanged = plan.Actions.Single(action => action.RelativePath == target.CompositionProjectPath).Kind
                != GenerationWriteActionKind.Unchanged;
            var composition = CompositionIntegrationBuildProjection.Create(
                GenerationWorkspacePath.ResolveFile(root, target.CompositionProjectPath), moduleProject,
                GenerationWorkspacePath.ResolveFile(root, target.CompositionCatalogPath), catalog, projectChanged,
                Path.Combine(temporary, "composition"));
            foreach (var file in module.SourceFiles.Concat(composition.SourceFiles))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file.FullPath)!);
                await File.WriteAllTextAsync(file.FullPath, file.Content, new UTF8Encoding(false), cancellationToken);
            }
            var targets = Path.Combine(temporary, "FullNet.FullIntegration.targets");
            var document = new XDocument(new XElement("Project",
                XDocument.Parse(module.TargetsContent).Root!.Elements(),
                XDocument.Parse(composition.TargetsContent).Root!.Elements()));
            await File.WriteAllTextAsync(targets, document.ToString(), cancellationToken);
            var build = await RunAsync(root,
            [
                "build", composition.CompositionProjectFullPath, "--configuration", "Release", "--nologo",
                "--artifacts-path", Path.Combine(temporary, "build"),
                "-p:CustomAfterMicrosoftCommonTargets=" + targets,
                "-p:FullNetModuleIntegrationProject=" + moduleProject,
                "-p:FullNetCompositionIntegrationProject=" + composition.CompositionProjectFullPath,
            ], cancellationToken);
            if (build.Code == 0) return ModuleIntegrationCompilationResult.Success();
            var errors = build.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.Contains("error ", StringComparison.OrdinalIgnoreCase))
                .Select(line => line.Replace(root, "<workspace>", StringComparison.OrdinalIgnoreCase)
                    .Replace(temporary, "<temporary>", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.Ordinal).Take(20).ToArray();
            return ModuleIntegrationCompilationResult.Failure(errors.Length == 0 ? ["整链接入编译失败。"] : errors);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or TimeoutException)
        {
            return ModuleIntegrationCompilationResult.Failure(["整链接入构建工具不可用或执行超时。"]);
        }
        finally
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
        }
    }

    private static async Task<(int Code, string Output)> RunAsync(string root, string[] arguments, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        using var process = new Process { StartInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        } };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.StartInfo.Environment["UseSharedCompilation"] = "false";
        process.StartInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(output, error);
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException();
        }
        return (process.ExitCode, await output + "\n" + await error);
    }
}
