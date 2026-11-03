using Full.NET.Modules.CodeGeneration.Configuration;
using Full.NET.Modules.CodeGeneration.Contracts;

namespace Full.NET.UnitTests.CodeGeneration;

/// <summary>单元测试（代码生成）：Code Generation Apply Options。</summary>
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
    public void Apply_contract_binds_reviewed_plan_hash_and_persistent_idempotency_key()
    {
        var previewRunId = Guid.Parse(
            "0198f36e-f7a7-7c52-9cbb-774e67411212");
        var key = Guid.CreateVersion7();
        var request = new CodeGenerationRunApplyRequest(previewRunId, new string('a', 64), key);

        Assert.AreEqual(previewRunId, request.PlanId);
        Assert.AreEqual(key, request.IdempotencyKey);
    }
}
