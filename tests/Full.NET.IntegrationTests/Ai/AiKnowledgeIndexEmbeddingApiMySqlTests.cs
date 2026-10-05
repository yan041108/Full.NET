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
    public async Task Complete_text_execution_renews_lease_and_resumes_paid_batches_async()
    {
        var clock = new AiOperationBudgetAssertions.TestClock();
        using var factory = new FullNetApiFactory(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            configureTestServices: services => { services.RemoveAll<IClock>(); services.AddSingleton<IClock>(clock); });
        await AiKnowledgeIndexEmbeddingAssertions.VerifyAsync(factory, clock, wholeGeneration: true, executeGeneration: true);
    }

    [TestMethod]
    public async Task Text_generation_activation_is_atomic_and_revalidates_retries_async()
    {
        var clock = new AiOperationBudgetAssertions.TestClock();
        using var factory = new FullNetApiFactory(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            configureTestServices: services => { services.RemoveAll<IClock>(); services.AddSingleton<IClock>(clock); });
        await AiKnowledgeIndexEmbeddingAssertions.VerifyAsync(factory, clock, wholeGeneration: true, activateGeneration: true);
    }
    [TestMethod]
    public async Task Whole_text_generation_rejects_partial_and_changed_batches_async()
    {
        var clock = new AiOperationBudgetAssertions.TestClock();
        using var factory = new FullNetApiFactory(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            configureTestServices: services => { services.RemoveAll<IClock>(); services.AddSingleton<IClock>(clock); });
        await AiKnowledgeIndexEmbeddingAssertions.VerifyAsync(factory, clock, wholeGeneration: true);
    }

    [TestMethod]
    public async Task Text_execution_preserves_budget_and_dispatch_boundaries_async()
    {
        var clock = new AiOperationBudgetAssertions.TestClock();
        using var factory = new FullNetApiFactory(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            configureTestServices: services => { services.RemoveAll<IClock>(); services.AddSingleton<IClock>(clock); });
        await AiKnowledgeIndexEmbeddingAssertions.VerifyAsync(factory, clock);
    }
}
