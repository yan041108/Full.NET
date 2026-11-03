using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>正式 Native API 的普通文档、原文、索引、发布、搜索和历史拒读边界。</summary>
[TestClass] [DoNotParallelize]
public sealed class NativeKnowledgeFaqOrdinaryBoundaryE2ETests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public Task Native_faq_bound_document_rejects_ordinary_routes_without_new_charges(DatabaseProvider provider) =>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider, true, verifyFaqOrdinaryBoundary: true);
}
