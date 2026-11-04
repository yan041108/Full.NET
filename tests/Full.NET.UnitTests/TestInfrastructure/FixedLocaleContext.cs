using Full.NET.Localization;
namespace Full.NET.UnitTests.TestInfrastructure;
/// <summary>为非语言场景固定规范语言，避免测试依赖运行机器区域设置。</summary>
internal sealed class FixedLocaleContext : ILocaleContext
{
    public string CurrentLocale => "zh-CN";
}
