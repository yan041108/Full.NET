using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>MySQL PDF 清单真实持久化、恢复、回滚与清理。</summary>
[TestClass]
public sealed class AiKnowledgeIndexPdfManifestApiMySqlTests
{
    [TestMethod]
    public async Task Pdf_manifest_persistence_preserves_task_session_and_source_boundaries()
    {
        var clock = new AiOperationBudgetAssertions.TestClock();
        using var factory = new FullNetApiFactory(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            configureTestServices: services => { services.RemoveAll<IClock>(); services.AddSingleton<IClock>(clock); });
        await AiKnowledgeIndexPdfManifestAssertions.VerifyAsync(factory, clock);
    }
}
