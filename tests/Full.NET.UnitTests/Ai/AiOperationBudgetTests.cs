using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Modules.Ai.Budgets;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.UnitTests.Ai;

/// <summary>费用不使用浮点数，未知计量不得变成免费，异常计量不能释放预留。</summary>
[TestClass]
public sealed class AiOperationBudgetTests
{
    [TestMethod]
    public void Price_uses_version_currency_cached_input_and_rounds_up()
    {
        var price = new AiModelPrice(Guid.NewGuid(), "USD", 2m, 4m, 1m);
        Assert.AreEqual(0.000007m, AiExecutionBudget.CalculateCost(price, new(2, 1, 1)));
        Assert.AreEqual(0.00000001m, AiExecutionBudget.CalculateCost(price with { InputPerMillion = 0.000001m, CachedInputPerMillion = 0m }, new(1, 0)));
        Assert.IsNull(AiExecutionBudget.CalculateCost(null, new(1, 1)));
        Assert.IsNull(AiExecutionBudget.CalculateCost(price, new(null, 1)));
    }

    [TestMethod]
    [DataRow(-1L, 0L, 0L)]
    [DataRow(0L, -1L, 0L)]
    [DataRow(1L, 0L, -1L)]
    [DataRow(1L, 0L, 2L)]
    [DataRow(long.MaxValue, 1L, 0L)]
    public void Invalid_usage_is_rejected(long input, long output, long cached) =>
        Assert.Throws<AiBudgetException>(() => AiExecutionBudget.ValidateUsage(new(input, output, cached)));

    [TestMethod]
    [DataRow("endpoint")]
    [DataRow("organization")]
    [DataRow("version")]
    public void Chat_digest_changes_when_model_routing_or_revision_changes(string change)
    {
        var id = Guid.NewGuid();
        AiModelConfigRecord Model(bool changed) => new()
        {
            Id = id, ProviderKey = "openai", ModelId = "model", EndpointBaseUrl = changed && change == "endpoint" ? "https://other.test" : "https://original.test",
            OrganizationId = changed && change == "organization" ? "other-org" : "org", Version = changed && change == "version" ? 2 : 1
        };
        var operation = Guid.NewGuid();
        var first = AiChatBudgetRequest.Create(operation, Model(false), [("user", "hello")]);
        var second = AiChatBudgetRequest.Create(operation, Model(true), [("user", "hello")]);
        Assert.AreNotEqual(AiOperationBudgetStore.Fingerprint(first), AiOperationBudgetStore.Fingerprint(second));
    }

    [TestMethod]
    public void Invalid_price_cannot_produce_a_credit_or_overflow()
    {
        var price = new AiModelPrice(Guid.NewGuid(), "USD", 2m, 4m, 1m);
        foreach (var invalid in new[] { price with { InputPerMillion = -1 }, price with { Currency = "usd" },
            price with { CachedInputPerMillion = 3 }, price with { OutputPerMillion = decimal.MaxValue } })
            Assert.Throws<AiBudgetException>(() => AiExecutionBudget.CalculateCost(invalid, new(1, 1)));
    }
}
