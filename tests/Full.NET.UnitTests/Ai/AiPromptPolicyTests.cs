using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;

namespace Full.NET.UnitTests.Ai;

[TestClass]
public sealed class AiPromptPolicyTests
{
    private static AiPromptBody Body(string template = "请回答：{{input}}") => new(Guid.CreateVersion7(), "zh-CN", template, 100, 100);

    [TestMethod]
    public void Canonical_text_policy_is_valid() => Assert.IsTrue(AiPromptPolicy.IsValid(Body()));

    [TestMethod]
    public void Missing_model_is_rejected() => Assert.IsFalse(AiPromptPolicy.IsValid(Body() with { ModelConfigId = Guid.Empty }));

    [TestMethod]
    public void Unknown_or_noncanonical_language_is_rejected()
    {
        foreach (var language in new[] { "zh-cn", "en-us", "en", "fr-FR", "", "en-US " })
            Assert.IsFalse(AiPromptPolicy.IsValid(Body() with { LanguageTag = language }));
        Assert.IsTrue(AiPromptPolicy.IsValid(Body() with { LanguageTag = "en-US" }));
    }

    [TestMethod]
    public void Missing_repeated_or_unknown_placeholder_is_rejected()
    {
        foreach (var template in new[] { "input", "{{input}}{{input}}", "{{input}} {{secret}}" })
            Assert.IsFalse(AiPromptPolicy.IsValid(Body(template)));
    }

    [TestMethod]
    public void Template_and_token_limits_are_bounded()
    {
        Assert.IsFalse(AiPromptPolicy.IsValid(Body(new string('x', 8192) + "{{input}}")));
        foreach (var limit in new long[] { -1, 0, long.MaxValue })
        {
            Assert.IsFalse(AiPromptPolicy.IsValid(Body() with { InputTokenLimit = limit }));
            Assert.IsFalse(AiPromptPolicy.IsValid(Body() with { OutputTokenLimit = limit }));
        }
    }

    [TestMethod]
    public void Invalid_unicode_is_rejected() => Assert.IsFalse(AiPromptPolicy.IsValid(Body("{{input}}\ud800")));

    [TestMethod]
    public void Input_is_substituted_once_and_never_interpreted() =>
        Assert.AreEqual("请回答：{{input}} {{secret}}", AiPromptPolicy.Compose(Body(), "{{input}} {{secret}}"));

    [TestMethod]
    public void Composed_input_is_bounded_and_invalid_input_is_rejected()
    {
        Assert.IsNull(AiPromptPolicy.Compose(Body(), new string('x', 16_384)));
        Assert.IsNull(AiPromptPolicy.Compose(Body(), ""));
        Assert.IsNull(AiPromptPolicy.Compose(Body(), "\ud800"));
    }
}
