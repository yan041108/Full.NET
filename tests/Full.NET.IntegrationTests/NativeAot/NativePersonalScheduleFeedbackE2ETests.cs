using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>实际Native API与Host受控TLS模型，租户只读真实来源历史；转换零新增派发。</summary>
[TestClass] [DoNotParallelize] [TestCategory("NativeAotE2E")]
public sealed class NativePersonalScheduleFeedbackE2ETests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public Task Native_business_feedback_preserves_uncited_sources_and_independent_review(DatabaseProvider provider) =>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider, true, verifySearchAnswer: true,
            verifyPersonalScheduleAnswer: true, verifyPersonalScheduleFeedback: true);
}
