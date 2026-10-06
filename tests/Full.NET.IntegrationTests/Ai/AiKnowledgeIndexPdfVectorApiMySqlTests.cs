using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>MySql PDF 向量真实持久化、操作结算守卫与恢复。</summary>
[TestClass]
public sealed class AiKnowledgeIndexPdfVectorApiMySqlTests
{
    [TestMethod]
    public async Task Pdf_vectors_require_complete_manifest_and_settled_operation()
    {
        var clock = new AiOperationBudgetAssertions.TestClock();
        using var factory = new FullNetApiFactory(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            configureTestServices: services => { services.RemoveAll<IClock>(); services.AddSingleton<IClock>(clock); });
        await AiKnowledgeIndexPdfVectorAssertions.VerifyAsync(factory, clock);
    }
}
