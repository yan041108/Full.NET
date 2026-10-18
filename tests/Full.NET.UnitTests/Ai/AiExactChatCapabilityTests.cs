using Full.NET.AI.Abstractions.Budgets;
using Full.NET.AI.Abstractions.Models;

namespace Full.NET.UnitTests.Ai;

/// <summary>预算阶段只确认可信计数能力，真实协议与请求仍须在派发前取得证书。</summary>
[TestClass]
public sealed class AiExactChatCapabilityTests
{
    [TestMethod]
    public void Approved_capability_is_frozen_but_does_not_authorize_an_unknown_protocol()
    {
        var entries = new List<AiExactChatCapability> { new("openai_compatible", "approved-model") };
        var policy = new AiChatInputAdmissionPolicy(true, entries); entries.Clear();
        policy.RequireBudgetReservation("openai_compatible", "approved-model");
        policy.RequireChatDispatch(true);
        Assert.ThrowsExactly<AiBudgetException>(() => policy.RequireChatDispatch(false));
        Assert.ThrowsExactly<AiBudgetException>(() => policy.RequireBudgetReservation("openai_compatible", "other"));
        Assert.ThrowsExactly<AiBudgetException>(() => policy.RequireBudgetReservation("ollama", "approved-model"));
    }

    [TestMethod]
    [DataRow("openai_compatible", "*")] [DataRow("*", "model")] [DataRow("", "model")] [DataRow("openai_compatible", " ")]
    public void Wildcard_or_invalid_capabilities_cannot_certify_requests(string provider, string model)
        => Assert.ThrowsExactly<ArgumentException>(() => new AiChatInputAdmissionPolicy(true, [new(provider, model)]));
}
