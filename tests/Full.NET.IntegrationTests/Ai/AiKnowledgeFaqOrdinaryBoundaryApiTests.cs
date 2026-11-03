using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.NativeAot;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>已存在物化、索引和发布记录时，FAQ 绑定仍拒绝全部普通内容入口。</summary>
[TestClass] [DoNotParallelize]
public sealed class AiKnowledgeFaqOrdinaryBoundaryApiTests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public Task Faq_bound_document_rejects_ordinary_routes_and_dispatch_without_new_charges(DatabaseProvider provider) =>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider, false, verifyFaqOrdinaryBoundary: true);
}
