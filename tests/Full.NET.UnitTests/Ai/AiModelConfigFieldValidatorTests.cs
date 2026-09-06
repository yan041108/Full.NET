using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;

namespace Full.NET.UnitTests.Ai;

[TestClass]
public sealed class AiModelConfigFieldValidatorTests
{
    [TestMethod]
    public void ValidateMetadata_accepts_openai_compatible_defaults()
    {
        var message = AiModelConfigFieldValidator.ValidateMetadata(
            "GPT-4o Mini",
            AiProviderKeys.OpenAiCompatible,
            AiModelConfigFieldValidator.DefaultOpenAiCompatibleEndpoint,
            "gpt-4o-mini",
            null);

        Assert.IsNull(message);
    }

    [TestMethod]
    public void ValidateMetadata_rejects_endpoint_with_credentials()
    {
        var message = AiModelConfigFieldValidator.ValidateMetadata(
            "Local",
            AiProviderKeys.Ollama,
            "http://user:pass@127.0.0.1:11434",
            "llama3",
            null);

        Assert.IsNotNull(message);
    }

    [TestMethod]
    public void ValidateQuotaLimits_rejects_negative_token_limit()
    {
        var message = AiModelConfigFieldValidator.ValidateQuotaLimits(-1, 100);

        Assert.IsNotNull(message);
    }
}
