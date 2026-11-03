using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.NativeAot;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>在复用原生发布前先核对同一真实 HTTP、Identity、Files 与人工完整代次场景，避免将业务失败误当裁剪故障。</summary>
[TestClass]
[DoNotParallelize]
public sealed class AiKnowledgeHumanPublicationApiTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public Task Human_generation_publication_and_v4_history_preserve_complete_provenance(DatabaseProvider provider) =>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider, false);
}
