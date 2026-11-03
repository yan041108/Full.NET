using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Ai;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>本人日程权威来源的真实 Native API；不派发模型或伪装整组业务助手完成。</summary>
[TestClass][DoNotParallelize]
public sealed class NativePersonalScheduleSourceE2ETests
{
    [TestMethod][DataRow(DatabaseProvider.SqlServer)][DataRow(DatabaseProvider.MySql)]
    public Task Native_personal_schedule_sources_preserve_current_owner_scope(DatabaseProvider provider) =>
        PersonalScheduleSourceScenario.VerifyAsync(provider, true);
}
