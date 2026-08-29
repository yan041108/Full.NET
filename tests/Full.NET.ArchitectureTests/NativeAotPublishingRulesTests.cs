using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Full.NET.ArchitectureTests;

/// <summary>
/// 验证 Native AOT 发布边界：仅已批准的 API/Worker 宿主可在显式发布模式下设置 SDK <c>PublishAot</c>，
/// 且静态分析通过自有 <c>FullNetAotAnalysis</c> 属性启用，不污染 Migrator 与 netstandard2.0 生成器项目。
/// </summary>
[TestClass]
public sealed class NativeAotPublishingRulesTests
{
    private static readonly string[] ProjectsThatMustNotPublishAot =
    [
        "src/Generators/Full.NET.Messaging.Generators/Full.NET.Messaging.Generators.csproj",
        "src/Hosts/Full.NET.Host.Migrator/Full.NET.Host.Migrator.csproj",
    ];

    [TestMethod]
    public void ApiHost_DefaultPublishMode_DoesNotEnableSdkPublishAot()
    {
        var publishAot = EvaluateMsBuildProperty(
            "src/Hosts/Full.NET.Host.Api/Full.NET.Host.Api.csproj",
            "PublishAot");

        Assert.IsFalse(
            IsTruthyMsBuildValue(publishAot),
            $"默认 API 宿主不得设置 PublishAot=true，实际值：'{publishAot}'.");
    }

    [TestMethod]
    public void ApiHost_NativeAotPublishMode_EnablesSdkPublishAot()
    {
        var publishAot = EvaluateMsBuildProperty(
            "src/Hosts/Full.NET.Host.Api/Full.NET.Host.Api.csproj",
            "PublishAot",
            [("-p:FullNetPublishMode=NativeAot", null)]);

        Assert.IsTrue(
            IsTruthyMsBuildValue(publishAot),
            $"FullNetPublishMode=NativeAot 时 API 必须设置 PublishAot=true，实际值：'{publishAot}'.");
    }

    [TestMethod]
    public void UnapprovedProjects_DoNotInheritSdkPublishAot_WhenPublishModeIsNativeAot()
    {
        foreach (var relativePath in ProjectsThatMustNotPublishAot)
        {
            var publishAot = EvaluateMsBuildProperty(
                relativePath,
                "PublishAot",
                [("-p:FullNetPublishMode=NativeAot", null)]);

            Assert.IsFalse(
                IsTruthyMsBuildValue(publishAot),
                $"{relativePath} 不得继承 PublishAot=true，实际值：'{publishAot}'.");
        }
    }

    [TestMethod]
    public void RepositoryRoot_DoesNotGloballySetPublishAot()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var directoryBuildProps = File.ReadAllText(
            Path.Combine(root, "Directory.Build.props"));
        var directoryBuildTargets = File.Exists(Path.Combine(root, "Directory.Build.targets"))
            ? File.ReadAllText(Path.Combine(root, "Directory.Build.targets"))
            : string.Empty;

        Assert.IsFalse(
            ContainsUnconditionalPublishAot(directoryBuildProps),
            "Directory.Build.props 不得无条件设置 PublishAot。");
        Assert.IsFalse(
            ContainsUnconditionalPublishAot(directoryBuildTargets),
            "Directory.Build.targets 不得无条件设置 PublishAot。");
    }

    [TestMethod]
    public void FullNetAotAnalysis_EnablesAnalyzersOnlyForNet8PlusProjects()
    {
        var apiEnableAot = EvaluateMsBuildProperty(
            "src/Hosts/Full.NET.Host.Api/Full.NET.Host.Api.csproj",
            "EnableAotAnalyzer",
            [("-p:FullNetAotAnalysis=true", null)]);
        var apiEnableTrim = EvaluateMsBuildProperty(
            "src/Hosts/Full.NET.Host.Api/Full.NET.Host.Api.csproj",
            "EnableTrimAnalyzer",
            [("-p:FullNetAotAnalysis=true", null)]);

        Assert.IsTrue(
            IsTruthyMsBuildValue(apiEnableAot),
            "net10.0 API 项目在 FullNetAotAnalysis=true 时必须启用 EnableAotAnalyzer。");
        Assert.IsTrue(
            IsTruthyMsBuildValue(apiEnableTrim),
            "net10.0 API 项目在 FullNetAotAnalysis=true 时必须启用 EnableTrimAnalyzer。");
    }

    [TestMethod]
    public void FullNetAotAnalysis_DoesNotEnableAnalyzersForNetStandardGenerator()
    {
        var enableAot = EvaluateMsBuildProperty(
            "src/Generators/Full.NET.Messaging.Generators/Full.NET.Messaging.Generators.csproj",
            "EnableAotAnalyzer",
            [("-p:FullNetAotAnalysis=true", null)]);
        var enableTrim = EvaluateMsBuildProperty(
            "src/Generators/Full.NET.Messaging.Generators/Full.NET.Messaging.Generators.csproj",
            "EnableTrimAnalyzer",
            [("-p:FullNetAotAnalysis=true", null)]);

        Assert.IsFalse(
            IsTruthyMsBuildValue(enableAot),
            "netstandard2.0 Messaging 生成器不得启用 EnableAotAnalyzer。");
        Assert.IsFalse(
            IsTruthyMsBuildValue(enableTrim),
            "netstandard2.0 Messaging 生成器不得启用 EnableTrimAnalyzer。");
    }

    private static bool ContainsUnconditionalPublishAot(string content)
    {
        // 仅拒绝无 Condition 的 PublishAot 赋值；允许 API 项目内带条件的 NativeAot 转换。
        return Regex.IsMatch(
            content,
            @"<PublishAot>\s*true\s*</PublishAot>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool IsTruthyMsBuildValue(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static string EvaluateMsBuildProperty(
        string relativeProjectPath,
        string propertyName,
        IReadOnlyList<(string Argument, string? Value)>? extraArguments = null)
    {
        var root = ArchitectureRepositoryRoot.Find();
        var projectPath = Path.Combine(
            root,
            relativeProjectPath.Replace('/', Path.DirectorySeparatorChar));

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
            $"MSBuild 评估 {relativeProjectPath}::{propertyName} 失败：{error}");

        return output;
    }
}
