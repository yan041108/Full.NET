using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.NativeAot;
namespace Full.NET.IntegrationTests.Ai;

/// <summary>受控 JIT 模型、真实数据库与公开混合问答，旧发布仅作为准备。</summary>
[TestClass] [DoNotParallelize]
public sealed class AiPersonalScheduleAnswerApiTests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public Task Mixed_answer_keeps_all_personal_schedule_dependencies(DatabaseProvider provider) =>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider, false, verifySearchAnswer: true, verifyPersonalScheduleAnswer: true);
}
