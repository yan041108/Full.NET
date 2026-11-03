using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>首个真实业务来源的双库 Host/Tenant、精确用途与完整批次。</summary>
[TestClass][DoNotParallelize]
public sealed class AiPersonalScheduleSourceApiTests
{
    [TestMethod][DataRow(DatabaseProvider.SqlServer)][DataRow(DatabaseProvider.MySql)]
    public Task Personal_schedule_sources_preserve_current_owner_scope_and_complete_projection(DatabaseProvider provider) =>
        PersonalScheduleSourceScenario.VerifyAsync(provider, false);
}
