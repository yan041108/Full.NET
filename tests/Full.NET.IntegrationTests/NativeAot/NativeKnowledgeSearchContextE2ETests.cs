using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>只验证新增原生上下文路径与同一场景双库证明，可连接正式 Vue。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeKnowledgeSearchContextE2ETests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public Task Native_context_windows_preserve_scope_positions_and_frozen_source(DatabaseProvider provider) =>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider, true, verifyContext: true);
}
