using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>实际原生公开问答消费受控 TLS 模型，保留正式目的地与计量边界。</summary>
[TestClass] [DoNotParallelize]
public sealed class NativeKnowledgeSearchAnswerE2ETests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public Task Native_search_answer_freezes_publication_and_revalidates_history(DatabaseProvider provider) =>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider, true, verifySearchAnswer: true);
}
