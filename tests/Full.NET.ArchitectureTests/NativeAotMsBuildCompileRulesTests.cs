using System.Diagnostics;

namespace Full.NET.ArchitectureTests;

/// <summary>
/// 验证 NativeAot / FullNetAotAnalysis 与 Directory.Build.targets 在跨平台路径下共享同一套编译条件。
/// </summary>
[TestClass]
public sealed class NativeAotMsBuildCompileRulesTests
{
    [TestMethod]
    public void DirectoryBuildTargets_DoesNotUseWindowsPathSeparatorsInConditions()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var targets = File.ReadAllText(Path.Combine(root, "Directory.Build.targets"));

        Assert.IsFalse(
            targets.Contains("\\BuildingBlocks\\", StringComparison.Ordinal),
            "Directory.Build.targets 不得在条件中使用 Windows 路径分隔符。");
        Assert.IsFalse(
            targets.Contains("\\Modules\\", StringComparison.Ordinal),
            "Directory.Build.targets 不得在条件中使用 Windows 路径分隔符。");
        StringAssert.Contains(targets, ".Replace('\\', '/')");
    }

    [TestMethod]
    public void RealtimeProject_JitPublishMode_DoesNotDefineMessagePackConstant()
    {
        var defineConstants = EvaluateMsBuildProperty(
            "src/BuildingBlocks/Full.NET.Realtime.SignalR/Full.NET.Realtime.SignalR.csproj",
            "DefineConstants",
            []);

        Assert.IsFalse(
            ContainsDefineConstant(defineConstants, "FULLNET_SIGNALR_MESSAGEPACK"),
            "JIT 发布闭包不得定义 FULLNET_SIGNALR_MESSAGEPACK。");
    }

    [TestMethod]
    public void RealtimeProject_NativeAotPublishMode_DoesNotDefineMessagePackConstant()
    {
        var defineConstants = EvaluateMsBuildProperty(
            "src/BuildingBlocks/Full.NET.Realtime.SignalR/Full.NET.Realtime.SignalR.csproj",
            "DefineConstants",
            [("-p:FullNetPublishMode=NativeAot", null)]);

        Assert.IsFalse(
            ContainsDefineConstant(defineConstants, "FULLNET_SIGNALR_MESSAGEPACK"),
            "NativeAot 发布闭包不得定义 FULLNET_SIGNALR_MESSAGEPACK。");
    }

    [TestMethod]
    public void RealtimeProject_FullNetAotAnalysis_DoesNotDefineMessagePackConstant()
    {
        var defineConstants = EvaluateMsBuildProperty(
            "src/BuildingBlocks/Full.NET.Realtime.SignalR/Full.NET.Realtime.SignalR.csproj",
            "DefineConstants",
            [("-p:FullNetAotAnalysis=true", null)]);

        Assert.IsFalse(
            ContainsDefineConstant(defineConstants, "FULLNET_SIGNALR_MESSAGEPACK"),
            "FullNetAotAnalysis 闭包不得定义 FULLNET_SIGNALR_MESSAGEPACK。");
    }

    [TestMethod]
    public void ModuleProject_FullNetAotAnalysis_EnablesRequestDelegateGenerator()
    {
        var enabled = EvaluateMsBuildProperty(
            "src/Modules/Full.NET.Modules.Identity/Full.NET.Modules.Identity.csproj",
            "EnableRequestDelegateGenerator",
            [("-p:FullNetAotAnalysis=true", null)]);

        Assert.IsTrue(
            IsTruthyMsBuildValue(enabled),
            "FullNetAotAnalysis 必须为 API 可达模块启用 EnableRequestDelegateGenerator。");
    }

    [TestMethod]
    [DataRow(
        "src/BuildingBlocks/Full.NET.Hosting/Full.NET.Hosting.csproj",
        "EnableConfigurationBindingGenerator")]
    [DataRow(
        "src/BuildingBlocks/Full.NET.Realtime.SignalR/Full.NET.Realtime.SignalR.csproj",
        "EnableConfigurationBindingGenerator")]
    [DataRow(
        "src/Modules/Full.NET.Modules.Identity/Full.NET.Modules.Identity.csproj",
        "EnableConfigurationBindingGenerator")]
    public void ApiReachableProjects_NativeAotAndAnalysisShareCompileProperties(
        string relativeProjectPath,
        string propertyName)
    {
        var nativeAotValue = EvaluateMsBuildProperty(
            relativeProjectPath,
            propertyName,
            [("-p:FullNetPublishMode=NativeAot", null)]);
        var analysisValue = EvaluateMsBuildProperty(
            relativeProjectPath,
            propertyName,
            [("-p:FullNetAotAnalysis=true", null)]);

        Assert.AreEqual(
            nativeAotValue,
            analysisValue,
            $"{relativeProjectPath} 的 {propertyName} 在 NativeAot 与 FullNetAotAnalysis 下必须一致。");
        Assert.IsTrue(
            IsTruthyMsBuildValue(nativeAotValue),
            $"{relativeProjectPath} 在 NativeAot 下必须启用 {propertyName}。");
    }

    [TestMethod]
    public void LinuxStyleProjectDirectory_NormalizesForAotEligibility()
    {
        var root = ArchitectureRepositoryRoot.Find().Replace('\\', '/');
        var hostingProject = Path.Combine(
                root,
                "src/BuildingBlocks/Full.NET.Hosting/Full.NET.Hosting.csproj")
            .Replace('\\', '/');

        var enableAot = EvaluateMsBuildPropertyWithProjectPath(
            hostingProject,
            "EnableAotAnalyzer",
            [("-p:FullNetAotAnalysis=true", null)]);

        Assert.IsTrue(
            IsTruthyMsBuildValue(enableAot),
            "Linux 风格 MSBuildProjectDirectory 归一化后仍必须启用 EnableAotAnalyzer。");
    }

    private static bool ContainsDefineConstant(string? defineConstants, string constant) =>
        (defineConstants ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(entry => string.Equals(entry, constant, StringComparison.Ordinal));

    private static bool IsTruthyMsBuildValue(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static string EvaluateMsBuildProperty(
        string relativeProjectPath,
        string propertyName,
        IReadOnlyList<(string Argument, string? Value)>? extraArguments = null) =>
        EvaluateMsBuildPropertyWithProjectPath(
            Path.Combine(
                    ArchitectureRepositoryRoot.Find(),
                    relativeProjectPath.Replace('/', Path.DirectorySeparatorChar))
                .Replace('\\', '/'),
            propertyName,
            extraArguments);

    private static string EvaluateMsBuildPropertyWithProjectPath(
        string projectPath,
        string propertyName,
        IReadOnlyList<(string Argument, string? Value)>? extraArguments = null)
    {
        var arguments = new List<string>
        {
            "msbuild",
            projectPath,
            $"-getProperty:{propertyName}",
            "-nologo",
            "-v:q",
        };

        if (extraArguments is not null)
        {
            foreach (var (argument, _) in extraArguments)
            {
                arguments.Add(argument);
            }
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 dotnet msbuild。");
        var output = process.StandardOutput.ReadToEnd().Trim();
        var error = process.StandardError.ReadToEnd().Trim();
        process.WaitForExit();

        Assert.AreEqual(
            0,
            process.ExitCode,
            $"MSBuild 评估 {projectPath}::{propertyName} 失败：{error}");

        return output;
    }
}
