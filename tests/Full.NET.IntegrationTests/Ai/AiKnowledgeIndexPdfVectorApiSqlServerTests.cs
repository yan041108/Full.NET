using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>SqlServer PDF 向量真实持久化、操作结算守卫与恢复。</summary>
[TestClass]
public sealed class AiKnowledgeIndexPdfVectorApiSqlServerTests
{
    [TestMethod]
    public async Task Pdf_vectors_require_complete_manifest_and_settled_operation()
    {
        var clock = new AiOperationBudgetAssertions.TestClock(); var controls = new AiKnowledgeIndexPdfVectorAssertions.WorkerControls();
        using var factory = new FullNetApiFactory(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            configureTestServices: services => { services.RemoveAll<IClock>(); services.AddSingleton<IClock>(clock); controls.Register(services); });
        await AiKnowledgeIndexPdfVectorAssertions.VerifyAsync(factory, clock, controls);
    }
}
