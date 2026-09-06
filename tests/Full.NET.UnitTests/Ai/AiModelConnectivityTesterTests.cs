using Full.NET.Modules.Ai.Connectivity;

namespace Full.NET.UnitTests.Ai;

[TestClass]
public sealed class AiModelConnectivityTesterTests
{
    [TestMethod]
    public void ContainsModelId_detects_openai_models_array()
    {
        const string body = """{"data":[{"id":"gpt-4o-mini"}]}""";

        Assert.IsTrue(AiModelConnectivityTester.ContainsModelId(body, "gpt-4o-mini"));
    }

    [TestMethod]
    public void ContainsModelId_detects_ollama_models_array()
    {
        const string body = """{"models":[{"name":"llama3"}]}""";

        Assert.IsTrue(AiModelConnectivityTester.ContainsModelId(body, "llama3"));
    }

    [TestMethod]
    public void NormalizeBaseUrl_trims_trailing_slash()
    {
        Assert.AreEqual(
            "https://api.openai.com/v1",
            AiModelConnectivityTester.NormalizeBaseUrl("https://api.openai.com/v1/"));
    }
}
