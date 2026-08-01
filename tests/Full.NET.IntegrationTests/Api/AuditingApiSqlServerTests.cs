using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Auditing;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class AuditingApiSqlServerTests
{
    [TestMethod]
    public async Task Host_access_log_query_follows_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await AuditingAccessLogAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Host_operation_log_query_follows_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await AuditingOperationLogAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Host_exception_log_query_follows_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await AuditingExceptionLogAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Request_audit_batch_rolls_back_partial_write_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await AuditingBatchRollbackAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Audit_retention_deletes_expired_rows_in_fair_batches_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await AuditingRetentionAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Host_outbound_call_log_query_follows_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await AuditingOutboundCallAssertions.VerifyAsync(factory);
    }
}
