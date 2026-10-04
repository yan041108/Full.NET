using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>MySQL 生产批次预算边界。</summary>
[TestClass]
public sealed class AiKnowledgeIndexBudgetApiMySqlTests
{
    [TestMethod]
    public async Task Batch_budget_reuses_identity_and_preserves_unknown_usage_async()
    {
        var clock = new AiOperationBudgetAssertions.TestClock();
        using var factory = new FullNetApiFactory(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            configureTestServices: services => { services.RemoveAll<IClock>(); services.AddSingleton<IClock>(clock); });
        await AiKnowledgeIndexBudgetAssertions.VerifyAsync(factory, clock);
    }
}
