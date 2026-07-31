using Full.NET.Modules.CodeGeneration.Configuration;
using Full.NET.Modules.CodeGeneration.Contracts;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class CodeGenerationApplyOptionsTests
{
    [TestMethod]
    public void Disabled_apply_accepts_missing_workspace_root()
    {
        var result = new CodeGenerationApplyOptionsValidator().Validate(
            null,
            new CodeGenerationApplyOptions());

        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    public void Enabled_apply_requires_existing_absolute_local_directory()
    {
        var validator = new CodeGenerationApplyOptionsValidator();
        var existingDirectory = Directory.CreateTempSubdirectory(
            "fullnet-codegeneration-apply-");
        var existingFile = Path.GetTempFileName();

        try
        {
            Assert.IsTrue(validator.Validate(null, new CodeGenerationApplyOptions
            {
                Enabled = true,
            }).Failed);
            Assert.IsTrue(validator.Validate(null, new CodeGenerationApplyOptions
            {
                Enabled = true,
                WorkspaceRoot = "relative/workspace",
            }).Failed);
            Assert.IsTrue(validator.Validate(null, new CodeGenerationApplyOptions
            {
                Enabled = true,
                WorkspaceRoot = Path.Combine(
                    Path.GetTempPath(),
                    $"missing-{Guid.NewGuid():N}"),
            }).Failed);
            Assert.IsTrue(validator.Validate(null, new CodeGenerationApplyOptions
            {
                Enabled = true,
                WorkspaceRoot = existingFile,
            }).Failed);
            if (OperatingSystem.IsWindows())
            {
                Assert.IsTrue(validator.Validate(null, new CodeGenerationApplyOptions
                {
                    Enabled = true,
                    WorkspaceRoot = @"\\server\share\workspace",
                }).Failed);
            }

            Assert.IsTrue(validator.Validate(null, new CodeGenerationApplyOptions
            {
                Enabled = true,
                WorkspaceRoot = existingDirectory.FullName,
            }).Succeeded);
        }
        finally
        {
            existingDirectory.Delete(recursive: true);
            File.Delete(existingFile);
        }
    }

    [TestMethod]
    public void Apply_contract_uses_only_preview_run_identity()
    {
        var previewRunId = Guid.Parse(
            "0198f36e-f7a7-7c52-9cbb-774e67411212");
        var request = new CodeGenerationRunApplyRequest(previewRunId);

        Assert.AreEqual(previewRunId, request.PreviewRunId);
    }
}
