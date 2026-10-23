using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实格式样本在 MySql 的完整持久化和拒读恢复。</summary>
[TestClass]
public sealed class AiKnowledgeDocumentFormatsApiMySqlTests
{
    [TestMethod]
    [TestCategory("DocumentSandbox")]
    public async Task Real_format_sources_require_settled_complete_generations()
    {
        var clock = new AiOperationBudgetAssertions.TestClock();
        using var factory = new FullNetApiFactory(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            configureTestServices: services => { services.RemoveAll<IClock>(); services.AddSingleton<IClock>(clock); });
        await AiKnowledgeDocumentFormatPersistenceAssertions.VerifyAsync(factory, clock);
    }
}
