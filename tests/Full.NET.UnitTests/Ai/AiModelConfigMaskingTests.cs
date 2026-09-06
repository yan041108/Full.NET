using Full.NET.Modules.Ai.Domain;

namespace Full.NET.UnitTests.Ai;

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
