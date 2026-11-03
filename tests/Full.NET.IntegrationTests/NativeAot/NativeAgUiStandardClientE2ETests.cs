using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Ai;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>实际Native API承载标准AG-UI客户端读链路，不启动Worker或模型。</summary>
[TestClass][DoNotParallelize][TestCategory("NativeAotE2E")]
public sealed class NativeAgUiStandardClientE2ETests
{
    [TestMethod][DataRow(DatabaseProvider.SqlServer)][DataRow(DatabaseProvider.MySql)]
    public Task Native_standard_client_replay_and_current_session_are_read_only(DatabaseProvider provider) => AgUiStandardClientScenario.VerifyAsync(provider, true);
}
