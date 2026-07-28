using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Auditing;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class AuditingApiMySqlTests
{
    [TestMethod]
    public async Task Host_access_log_query_follows_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await AuditingAccessLogAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Host_operation_log_query_follows_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await AuditingOperationLogAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Host_exception_log_query_follows_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await AuditingExceptionLogAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Request_audit_batch_rolls_back_partial_write_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await AuditingBatchRollbackAssertions.VerifyAsync(factory);
    }
}
