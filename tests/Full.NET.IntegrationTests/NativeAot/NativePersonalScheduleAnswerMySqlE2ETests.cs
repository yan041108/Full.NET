using Full.NET.Data.Abstractions;
namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>只补验原未完成的MySQL范围，不重复已通过的SQL Server Native身份。</summary>
[TestClass] [DoNotParallelize] [TestCategory("NativeAotE2E")]
public sealed class NativePersonalScheduleAnswerMySqlE2ETests
{
    [TestMethod]
    public Task Native_mysql_mixed_answer_history_and_formal_browser() =>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(DatabaseProvider.MySql, true, verifySearchAnswer: true, verifyPersonalScheduleAnswer: true);
}
