using Full.NET.IntegrationTests.Ai;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>正式Vue桌面/移动使用两个本人会话与真实MySQL Native API。</summary>
[TestClass][DoNotParallelize]
public sealed class NativePersonalScheduleSourceBrowserE2ETests
{
    [TestMethod]
    public Task Native_personal_schedule_source_browser_bridge() => PersonalScheduleSourceBrowserScenario.VerifyAsync();
}
