using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>SQL Server 文件版本必须遵守相同授权和持久化规则。</summary>
[TestClass]
public sealed class AiKnowledgeFileVersionsApiSqlServerTests
{
    [TestMethod]
    public async Task SqlServer_tenant_import_survives_quota_response_loss_and_worker_recovery()
    {
        var recovery = new AiKnowledgeTenantImportRecovery();
        using var factory = new FullNetApiFactory(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(), configureTestServices: recovery.Configure);
        await AiKnowledgeTenantImportRecovery.VerifyAsync(factory);
    }
    [TestMethod]
    public async Task SqlServer_import_recovery_preserves_unknown_commit_and_deleted_cleanup()
    {
        var faults = new AiKnowledgeImportFaults();
        using var factory = new FullNetApiFactory(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(), configureTestServices: faults.Configure);
        await AiKnowledgeImportFaults.VerifyAsync(factory, faults);
    }
    [TestMethod]
    public async Task SqlServer_knowledge_versions_upload_and_read_only_authorized_source()
    {
        using var factory = new FullNetApiFactory(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
        await AiKnowledgeFileVersionsApiAssertions.VerifyAsync(factory);
    }
}
