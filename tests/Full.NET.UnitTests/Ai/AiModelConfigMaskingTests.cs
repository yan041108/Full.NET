using Full.NET.Modules.Ai.Domain;

namespace Full.NET.UnitTests.Ai;

/// <summary>单元测试 AI：Model Config Masking。</summary>
[TestClass]
public sealed class AiModelConfigMaskingTests
{
    [TestMethod]
    public void MaskEndpoint_hides_host_middle_segment()
    {
        var masked = AiModelConfigMasking.MaskEndpoint("https://api.openai.com/v1");

        Assert.Contains("***", masked);
        Assert.DoesNotContain("openai", masked);
    }
}
