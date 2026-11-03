using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.NativeAot;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>完整发布下的公开上下文、替换冲突和撤权；不重跑旧搜索或历史矩阵。</summary>
[TestClass]
[DoNotParallelize]
public sealed class AiKnowledgeSearchContextApiTests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public Task Published_context_windows_preserve_scope_positions_and_frozen_source(DatabaseProvider provider) =>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider, false, verifyContext: true);
}
