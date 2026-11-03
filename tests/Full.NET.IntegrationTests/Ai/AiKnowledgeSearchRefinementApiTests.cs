using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.NativeAot;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>公开受控实验的双库真实发布、收费与失败关闭。</summary>
[TestClass] [DoNotParallelize]
public sealed class AiKnowledgeSearchRefinementApiTests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public Task Refinement_retains_original_scope_and_independent_settled_stages(DatabaseProvider provider) =>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider, false, verifyRefinement: true);
}
