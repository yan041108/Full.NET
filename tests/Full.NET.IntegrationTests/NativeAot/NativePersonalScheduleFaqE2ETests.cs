using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>公开API为实际Native；准备及FAQ索引Worker为受控JIT，不冒称Native模型HTTP派发。</summary>
[TestClass] [DoNotParallelize] [TestCategory("NativeAotE2E")]
public sealed class NativePersonalScheduleFaqE2ETests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public Task Native_business_faq_origin_and_background_session_preserve_owner_only_boundary(DatabaseProvider provider) =>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider, true, verifyPersonalScheduleAnswer: true, verifyPersonalScheduleFaq: true);
}
