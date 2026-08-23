using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.Data.CodeGeneration.Integration;

/// <summary>
/// 保存一次隔离模块编译验证的稳定结果。
/// </summary>
public sealed class ModuleIntegrationCompilationResult
{
    private ModuleIntegrationCompilationResult(
        bool succeeded,
        IEnumerable<string> diagnostics)
    {
        Succeeded = succeeded;
        Diagnostics = new ReadOnlyCollection<string>(
            diagnostics.ToArray());
    }

    /// <summary>编译是否成功；失败时不得写盘任何业务文件。</summary>
    public bool Succeeded { get; }

    /// <summary>已脱敏的编译诊断集合（最多 20 条），路径替换为占位符。</summary>
    public IReadOnlyList<string> Diagnostics { get; }

    /// <summary>构造一个成功结果，诊断集合为空。</summary>
    public static ModuleIntegrationCompilationResult Success() =>
        new(true, []);

    /// <summary>构造一个失败结果，必须至少包含一条诊断信息。</summary>
    public static ModuleIntegrationCompilationResult Failure(
        IEnumerable<string> diagnostics) =>
        new(false, diagnostics);
}

/// <summary>
/// 将生成后端临时注入目标模块，并把全部构建工件限制在命令拥有的临时目录。
/// </summary>
public static class ModuleIntegrationCompilationCommand
{
    private const int MaximumDiagnostics = 20;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    /// <summary>
    /// 在临时目录中注入当前 Schema 的默认后端产物并执行 Release 编译验证。
    /// </summary>
    /// <param name="repositoryRoot">仓库根目录绝对路径</param>
    /// <param name="schema">待接入实体的 CRUD Schema</param>
    /// <param name="target">显式声明的模块接入目标</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>隔离编译结果，失败时包含脱敏诊断</returns>
    public static async Task<ModuleIntegrationCompilationResult> ValidateAsync(
        string repositoryRoot,
        FullNetCrudSchema schema,
        ModuleIntegrationTarget target,
        CancellationToken cancellationToken) =>
        await ValidateAsync(
            repositoryRoot,
            schema,
            target,
            sourcePathsToRemove: null,
            candidateArtifacts: null,
            entryCandidate: null,
            cancellationToken);

    internal static async Task<ModuleIntegrationCompilationResult> ValidateAsync(
        string repositoryRoot,
        FullNetCrudSchema schema,
        ModuleIntegrationTarget target,
        IReadOnlyList<string>? sourcePathsToRemove,
        IReadOnlyList<GeneratedArtifact>? candidateArtifacts,
        CancellationToken cancellationToken) =>
        await ValidateAsync(
            repositoryRoot,
            schema,
            target,
            sourcePathsToRemove,
            candidateArtifacts,
            entryCandidate: null,
            cancellationToken);

    internal static async Task<ModuleIntegrationCompilationResult> ValidateAsync(
        string repositoryRoot,
        FullNetCrudSchema schema,
        ModuleIntegrationTarget target,
        IReadOnlyList<string>? sourcePathsToRemove,
        CancellationToken cancellationToken) =>
        await ValidateAsync(
            repositoryRoot,
            schema,
            target,
            sourcePathsToRemove,
            candidateArtifacts: null,
            entryCandidate: null,
            cancellationToken);

    internal static async Task<ModuleIntegrationCompilationResult>
        ValidateEntryAsync(
            string repositoryRoot,
            FullNetCrudSchema schema,
            ModuleIntegrationTarget target,
            string moduleEntryFullPath,
            string desiredEntryContent,
            CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleEntryFullPath);
        ArgumentNullException.ThrowIfNull(desiredEntryContent);

        return await ValidateAsync(
            repositoryRoot,
            schema,
            target,
            sourcePathsToRemove: null,
            candidateArtifacts: null,
            new ModuleEntryCandidate(
                Path.GetFullPath(moduleEntryFullPath),
                desiredEntryContent),
            cancellationToken);
    }

    private static async Task<ModuleIntegrationCompilationResult> ValidateAsync(
        string repositoryRoot,
        FullNetCrudSchema schema,
        ModuleIntegrationTarget target,
        IReadOnlyList<string>? sourcePathsToRemove,
        IReadOnlyList<GeneratedArtifact>? candidateArtifacts,
        ModuleEntryCandidate? entryCandidate,
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

        if (!StringComparer.Ordinal.Equals(
                schema.RootNamespace,
                target.ModuleName)
            && !schema.RootNamespace.EndsWith(
                $".{target.ModuleName}",
                StringComparison.Ordinal))
        {
            return ModuleIntegrationCompilationResult.Failure(
                ["Schema 根命名空间与显式目标模块不匹配。"]);
        }

        var moduleProjectFullPath = Path.Combine(
            root,
            target.ModuleProjectPath.Replace(
                '/',
                Path.DirectorySeparatorChar));
        if (!File.Exists(moduleProjectFullPath))
        {
            return ModuleIntegrationCompilationResult.Failure(
                ["模块项目不存在，无法执行编译验证。"]);
        }

        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            $"fullnet-codegen-module-build-{Guid.NewGuid():N}");
        try
        {
            ModuleIntegrationBuildProjection projection;
            if (entryCandidate is not null)
            {
                projection =
                    ModuleIntegrationBuildProjection.CreateEntryCandidate(
                        schema,
                        moduleProjectFullPath,
                        entryCandidate.FullPath,
                        entryCandidate.Content,
                        temporaryRoot);
            }
            else
            {
                var effectiveCandidateArtifacts = candidateArtifacts
                    ?? [
                        .. ModuleIntegrationBackendWorkspace
                            .CreateArtifacts(schema),
                        ModuleIntegrationBackendWorkspace
                            .CreateRegistryArtifact(
                                schema,
                                [schema.ClrTypeName]),
                    ];
                var effectiveRemovalPaths = sourcePathsToRemove
                    ?? effectiveCandidateArtifacts
                        .Select(artifact => Path.Combine(
                            Path.GetDirectoryName(moduleProjectFullPath)!,
                            artifact.RelativePath.Replace(
                                '/',
                                Path.DirectorySeparatorChar)))
                        .ToArray();
                projection = ModuleIntegrationBuildProjection.Create(
                    schema,
                    moduleProjectFullPath,
                    temporaryRoot,
                    effectiveRemovalPaths,
                    effectiveCandidateArtifacts);
            }

            await WriteProjectionAsync(
                projection,
                cancellationToken);
            return await RunBuildAsync(
                root,
                temporaryRoot,
                projection,
                cancellationToken);
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
            {
                Directory.Delete(temporaryRoot, recursive: true);
            }
        }
    }

    private static async Task WriteProjectionAsync(
        ModuleIntegrationBuildProjection projection,
        CancellationToken cancellationToken)
    {
        foreach (var file in projection.SourceFiles)
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(file.FullPath)!);
            await File.WriteAllTextAsync(
                file.FullPath,
                file.Content,
                StrictUtf8,
                cancellationToken);
        }

        Directory.CreateDirectory(
            Path.GetDirectoryName(projection.TargetsPath)!);
        await File.WriteAllTextAsync(
            projection.TargetsPath,
            projection.TargetsContent,
            StrictUtf8,
            cancellationToken);
    }

    private static async Task<ModuleIntegrationCompilationResult> RunBuildAsync(
        string repositoryRoot,
        string temporaryRoot,
        ModuleIntegrationBuildProjection projection,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = CreateStartInfo(
                repositoryRoot,
                temporaryRoot,
                projection),
        };
        process.Start();
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw;
        }

        var output = await standardOutput;
        var error = await standardError;
        if (process.ExitCode == 0)
        {
            return ModuleIntegrationCompilationResult.Success();
        }

        return ModuleIntegrationCompilationResult.Failure(
            SanitizeDiagnostics(
                string.Concat(output, "\n", error),
                repositoryRoot,
                temporaryRoot));
    }

    private static ProcessStartInfo CreateStartInfo(
        string repositoryRoot,
        string temporaryRoot,
        ModuleIntegrationBuildProjection projection)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(projection.ModuleProjectFullPath);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add("--artifacts-path");
        startInfo.ArgumentList.Add(Path.Combine(
            temporaryRoot,
            "artifacts"));
        startInfo.ArgumentList.Add("--nologo");
        startInfo.ArgumentList.Add(
            $"-p:CustomAfterMicrosoftCommonTargets={projection.TargetsPath}");
        startInfo.ArgumentList.Add(
            "-p:FullNetModuleIntegrationProject="
            + projection.ModuleProjectFullPath);
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["NUGET_XMLDOC_MODE"] = "skip";
        return startInfo;
    }

    private static IReadOnlyList<string> SanitizeDiagnostics(
        string output,
        string repositoryRoot,
        string temporaryRoot)
    {
        var diagnostics = output
            .Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Contains(
                "error ",
                StringComparison.OrdinalIgnoreCase))
            .Select(line =>
            {
                var errorIndex = line.IndexOf(
                    "error ",
                    StringComparison.OrdinalIgnoreCase);
                return line[errorIndex..]
                    .Replace(
                        repositoryRoot,
                        "<repository>",
                        StringComparison.OrdinalIgnoreCase)
                    .Replace(
                        temporaryRoot,
                        "<temporary>",
                        StringComparison.OrdinalIgnoreCase);
            })
            .Distinct(StringComparer.Ordinal)
            .Take(MaximumDiagnostics)
            .ToArray();
        return diagnostics.Length == 0
            ? ["模块接入编译失败，构建进程未返回可公开的编译诊断。"]
            : diagnostics;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
        }
        catch (InvalidOperationException)
        {
            // 进程可能在取消与终止之间自行退出，此时无需再次处理。
        }
    }

    private sealed record ModuleEntryCandidate(
        string FullPath,
        string Content);
}
