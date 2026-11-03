using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.NativeAot;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>完整未引用业务依赖约束真实反馈审核和两种派生准入，双库各含Host/Tenant。</summary>
[TestClass] [DoNotParallelize]
public sealed class AiPersonalScheduleFeedbackApiTests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public Task Business_feedback_and_redaction_cannot_bypass_owner_only_sources(DatabaseProvider provider) =>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider, false, verifySearchAnswer: true,
            verifyPersonalScheduleAnswer: true, verifyPersonalScheduleFeedback: true);
}
