using System.Diagnostics;
using System.Text;

namespace Full.NET.CodeGeneration.Cli;

/// <summary>
/// 用临时 ProjectReference 和 Catalog 候选验证 Composition 的真实 Release 构建。
/// </summary>
internal static class CompositionIntegrationCompilationCommand
{
    private const int MaximumDiagnostics = 20;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static async Task<ModuleIntegrationCompilationResult> ValidateAsync(
        string repositoryRoot,
        string compositionProjectFullPath,
        string moduleProjectFullPath,
        string compositionCatalogFullPath,
        string desiredCatalogContent,
        bool includeModuleReference,
        CancellationToken cancellationToken)
    {
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            $"fullnet-codegen-composition-build-{Guid.NewGuid():N}");
        try
        {
            var projection =
                CompositionIntegrationBuildProjection.Create(
                    compositionProjectFullPath,
                    moduleProjectFullPath,
                    compositionCatalogFullPath,
                    desiredCatalogContent,
                    includeModuleReference,
                    temporaryRoot);
            foreach (var sourceFile in projection.SourceFiles)
            {
                Directory.CreateDirectory(
                    Path.GetDirectoryName(sourceFile.FullPath)!);
                await File.WriteAllTextAsync(
                    sourceFile.FullPath,
                    sourceFile.Content,
                    StrictUtf8,
                    cancellationToken);
            }

            await File.WriteAllTextAsync(
                projection.TargetsPath,
                projection.TargetsContent,
                StrictUtf8,
                cancellationToken);
            return await RunBuildAsync(
                repositoryRoot,
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

    private static async Task<ModuleIntegrationCompilationResult> RunBuildAsync(
        string repositoryRoot,
        string temporaryRoot,
        CompositionIntegrationBuildProjection projection,
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
        CompositionIntegrationBuildProjection projection)
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
        startInfo.ArgumentList.Add(
            projection.CompositionProjectFullPath);
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
            "-p:FullNetCompositionIntegrationProject="
            + projection.CompositionProjectFullPath);
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
            ? ["Composition 接入编译失败，构建进程未返回可公开的编译诊断。"]
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
}
