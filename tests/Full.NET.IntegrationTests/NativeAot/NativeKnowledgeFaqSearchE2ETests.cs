using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>实际 Native API 经受控 TLS 模型处理 FAQ；不把 JIT 结果当作原生运行。</summary>
[TestClass] [DoNotParallelize]
public sealed class NativeKnowledgeFaqSearchE2ETests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public Task Native_faq_search_answer_and_history_preserve_full_provenance(DatabaseProvider provider) =>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider, true, verifyFaqLifecycle: true, verifyFaqSearch: true);
}
