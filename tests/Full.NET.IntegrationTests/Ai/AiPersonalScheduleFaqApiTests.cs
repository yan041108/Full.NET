using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.NativeAot;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>只复用正式文档发布准备，验证新增本人日程来源的不可变FAQ和真实后台索引。</summary>
[TestClass] [DoNotParallelize]
public sealed class AiPersonalScheduleFaqApiTests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public Task Business_faq_provenance_survives_mutations_and_private_review_denies(DatabaseProvider provider) =>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider, false, verifyPersonalScheduleAnswer: true, verifyPersonalScheduleFaq: true);
}
