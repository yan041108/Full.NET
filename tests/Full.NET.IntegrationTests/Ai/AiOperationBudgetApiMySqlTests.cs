using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实 MySql 预算、价格及恢复数据路径。</summary>
[TestClass]
public sealed class AiOperationBudgetApiMySqlTests
{
    [TestMethod]
    public async Task Operation_budget_preserves_atomicity_and_accounting_async()
    {
        var clock = new AiOperationBudgetAssertions.TestClock();
        using var factory = new FullNetApiFactory(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            configureTestServices: services => { services.RemoveAll<IClock>(); services.AddSingleton<IClock>(clock); });
        await AiOperationBudgetAssertions.VerifyAsync(factory, clock);
    }
}
