using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.NativeAot;

namespace Full.NET.IntegrationTests.Ai;

[TestClass] [DoNotParallelize]
public sealed class AiKnowledgeFaqFeedbackApiTests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public Task Approved_private_feedback_is_a_non_degradable_faq_source(DatabaseProvider provider) =>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider, false, verifyFaqLifecycle: true, verifyFaqSearch: true, verifyFaqFeedback: true);
}
