using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Ai;
namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>正常链路为实际Native API与Worker，故障包装器补验使用明确隔离的受控JIT执行。</summary>
[TestClass][DoNotParallelize][TestCategory("NativeAotE2E")]
public sealed class NativePersonalScheduleCompletionE2ETests
{
    [TestMethod][DataRow(DatabaseProvider.SqlServer)][DataRow(DatabaseProvider.MySql)]
    public Task Native_completion_preserves_original_operation_and_receiver_fact(DatabaseProvider provider)=>PersonalScheduleCompletionScenario.VerifyAsync(provider,true);
}
