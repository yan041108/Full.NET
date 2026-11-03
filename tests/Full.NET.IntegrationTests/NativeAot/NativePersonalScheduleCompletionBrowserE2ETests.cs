using Full.NET.IntegrationTests.Ai;
namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>两端正式页面与独立审批使用实际MySQL Native API和Native Worker。</summary>
[TestClass][DoNotParallelize]
public sealed class NativePersonalScheduleCompletionBrowserE2ETests
{
    [TestMethod]
    public Task Native_personal_schedule_completion_browser_bridge()=>PersonalScheduleCompletionBrowserScenario.VerifyAsync();
}
