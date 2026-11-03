using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Ai;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>运行实际 ELF，以同一公开场景验证双库参数、物化及编辑/恢复 JSON 静态闭包；解析由生产 JIT Worker 执行。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeKnowledgeCorrectionEditingE2ETests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public Task Native_correction_edit_restore_history_and_original_source(DatabaseProvider provider) =>
        AiKnowledgeCorrectionEditingApiTests.Verify(provider, NativeApiArtifactLocator.RequireArtifact());
}
