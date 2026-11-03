using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.NativeAot;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>双库完整人工发布中的真实图片父子关系、收费不变及来源失效。</summary>
[TestClass] [DoNotParallelize]
public sealed class AiKnowledgeSearchRelationsApiTests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public Task Relations_follow_real_embedded_parent_without_model_calls(DatabaseProvider provider) =>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider, false, "docx-image", verifyRelations: true);
}
