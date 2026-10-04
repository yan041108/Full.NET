using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>MySQL 实际账本与执行调用边界。</summary>
[TestClass]
public sealed class AiKnowledgeIndexEmbeddingApiMySqlTests
{
    [TestMethod]
    public async Task Text_execution_preserves_budget_and_dispatch_boundaries_async()
    {
        var clock = new AiOperationBudgetAssertions.TestClock();
        using var factory = new FullNetApiFactory(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            configureTestServices: services => { services.RemoveAll<IClock>(); services.AddSingleton<IClock>(clock); });
        await AiKnowledgeIndexEmbeddingAssertions.VerifyAsync(factory, clock);
    }
}
