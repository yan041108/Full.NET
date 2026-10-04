using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>MySQL 实际会话绑定与事务恢复边界。</summary>
[TestClass]
public sealed class AiKnowledgeIndexSessionApiMySqlTests
{
    [TestMethod]
    public async Task Durable_session_creation_preserves_identity_and_atomicity_async()
    {
        var clock = new AiOperationBudgetAssertions.TestClock();
        using var factory = new FullNetApiFactory(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            configureTestServices: services => { services.RemoveAll<IClock>(); services.AddSingleton<IClock>(clock); });
        await AiKnowledgeIndexSessionAssertions.VerifyAsync(factory, clock);
    }
}
