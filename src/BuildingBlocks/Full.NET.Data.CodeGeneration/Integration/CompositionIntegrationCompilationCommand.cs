using System.Diagnostics;
using System.Text;

namespace Full.NET.Data.CodeGeneration.Integration;

/// <summary>
/// 用临时 ProjectReference 和 Catalog 候选验证 Composition 的真实 Release 构建。
/// </summary>
public static class CompositionIntegrationCompilationCommand
{
    private const int MaximumDiagnostics = 20;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    /// <summary>
    /// 用临时 ProjectReference 与候选 Catalog 验证 Composition 的真实 Release 构建。
    /// </summary>
    /// <param name="repositoryRoot">仓库根目录绝对路径</param>
    /// <param name="compositionProjectFullPath">真实 Composition 项目绝对路径</param>
    /// <param name="moduleProjectFullPath">目标模块项目绝对路径</param>
    /// <param name="compositionCatalogFullPath">真实 Catalog 绝对路径</param>
    /// <param name="desiredCatalogContent">候选 Catalog 内容</param>
    /// <param name="includeModuleReference">是否在临时 targets 中注入 ProjectReference</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>隔离编译结果，失败时包含脱敏诊断</returns>
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
        return CreateBuildResult(process.ExitCode, string.Concat(output, "\n", error), repositoryRoot, temporaryRoot);
    }

    // 成功只由进程退出状态决定；输出文本只能用于解释已经确认的失败。
    internal static ModuleIntegrationCompilationResult CreateBuildResult(
        int exitCode, string output, string repositoryRoot, string temporaryRoot)
    {
        if (exitCode == 0)
        {
            return ModuleIntegrationCompilationResult.Success();
        }

        return ModuleIntegrationCompilationResult.Failure(
            SanitizeDiagnostics(
                output,
                repositoryRoot,
                temporaryRoot,
                exitCode));
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
        string temporaryRoot,
        int exitCode)
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
            // 非编译器输出不能原样公开；退出码本身仍须保留，便于定位 SDK 或进程异常。
            ? [$"Composition 接入编译失败，构建进程未返回可公开的编译诊断。构建进程退出码：{exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)}。"]
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
