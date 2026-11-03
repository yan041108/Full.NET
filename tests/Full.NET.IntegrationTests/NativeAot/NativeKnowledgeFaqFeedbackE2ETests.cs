using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

[TestClass] [DoNotParallelize]
public sealed class NativeKnowledgeFaqFeedbackE2ETests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public Task Native_private_feedback_faq_rechecks_original_answer_and_every_source(DatabaseProvider provider) =>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider, true, verifyFaqLifecycle: true, verifyFaqSearch: true, verifyFaqFeedback: true);
}
