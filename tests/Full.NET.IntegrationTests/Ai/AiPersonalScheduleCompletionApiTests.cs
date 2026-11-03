using Full.NET.Data.Abstractions;
namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实双库静态运行、独立批准、准确回执与故障收敛。</summary>
[TestClass][DoNotParallelize]
public sealed class AiPersonalScheduleCompletionApiTests
{
    [TestMethod][DataRow(DatabaseProvider.SqlServer)][DataRow(DatabaseProvider.MySql)]
    public Task Completion_preserves_original_operation_and_independent_approval(DatabaseProvider provider)=>PersonalScheduleCompletionScenario.VerifyAsync(provider,false);
}
