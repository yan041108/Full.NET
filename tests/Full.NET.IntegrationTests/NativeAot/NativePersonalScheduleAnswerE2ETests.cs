using Full.NET.Data.Abstractions;
namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>实际 Native API 通过受控内部 TLS 模型消费独立日程引用。</summary>
[TestClass] [DoNotParallelize] [TestCategory("NativeAotE2E")]
public sealed class NativePersonalScheduleAnswerE2ETests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public Task Native_mixed_answer_and_history_keep_complete_business_sources(DatabaseProvider provider) =>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider, true, verifySearchAnswer: true, verifyPersonalScheduleAnswer: true);
}
