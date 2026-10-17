using System.Globalization;
using Full.NET.Data.CodeGeneration.Integration;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class IntegrationCompilationDiagnosticsTests
{
    [TestMethod]
    [DataRow(false, 1, "")]
    [DataRow(true, 1, "")]
    [DataRow(false, 2, "credential-probe")]
    [DataRow(true, 2, "credential-probe")]
    [DataRow(false, -1, "Build failed: credential-probe")]
    [DataRow(true, -1, "Build failed: credential-probe")]
    [DataRow(false, -1073741819, "Fatal runtime failure: credential-probe")]
    [DataRow(true, -1073741819, "Fatal runtime failure: credential-probe")]
    [DataRow(false, 1, "错误：credential-probe")]
    [DataRow(true, 1, "错误：credential-probe")]
    public void Missing_compiler_diagnostics_preserve_exit_code_without_echoing_output(bool composition, int exitCode, string output)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            // 退出码是跨机器诊断信息，数字格式不能随开发机器区域设置变化。
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            var result = BuildResult(composition, exitCode, output);
            Assert.IsFalse(result.Succeeded);
            Assert.HasCount(1, result.Diagnostics);
            StringAssert.Contains(result.Diagnostics[0], "构建进程退出码：" + exitCode.ToString(CultureInfo.InvariantCulture));
            StringAssert.Contains(result.Diagnostics[0], composition ? "Composition 接入编译失败" : "模块接入编译失败");
            Assert.IsFalse(result.Diagnostics[0].Contains("credential-probe", StringComparison.Ordinal));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [TestMethod]
    [DataRow(false, 1)]
    [DataRow(true, 1)]
    [DataRow(false, -1)]
    [DataRow(true, -1)]
    public void Compiler_diagnostics_preserve_redaction_deduplication_and_limit(bool composition, int exitCode)
    {
        var lines = Enumerable.Range(0, 30).Select(index =>
            $"/repository/source.cs: error CS{index:D4}: /REPOSITORY/source.cs /TEMPORARY/generated.cs").ToArray();
        var result = BuildResult(composition, exitCode, string.Join('\n', lines.Concat(lines)));
        Assert.IsFalse(result.Succeeded);
        Assert.HasCount(20, result.Diagnostics);
        CollectionAssert.AreEqual(Enumerable.Range(0, 20).Select(index =>
            $"error CS{index:D4}: <repository>/source.cs <temporary>/generated.cs").ToArray(), result.Diagnostics.ToArray());
    }

    [TestMethod]
    [DataRow(false, "")]
    [DataRow(true, "")]
    [DataRow(false, "error text from build logging")]
    [DataRow(true, "error text from build logging")]
    public void Successful_build_does_not_turn_logged_text_into_failure(bool composition, string output)
    {
        var result = BuildResult(composition, 0, output);
        Assert.IsTrue(result.Succeeded);
        Assert.HasCount(0, result.Diagnostics);
    }

    private static ModuleIntegrationCompilationResult BuildResult(bool composition, int exitCode, string output) =>
        composition ? CompositionIntegrationCompilationCommand.CreateBuildResult(exitCode, output, "/repository", "/temporary")
            : ModuleIntegrationCompilationCommand.CreateBuildResult(exitCode, output, "/repository", "/temporary");
}
