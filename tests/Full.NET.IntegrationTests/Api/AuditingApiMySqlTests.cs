using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Auditing;

namespace Full.NET.IntegrationTests.Api;

/// <summary>集成测试：AuditingApiMySql。</summary>
[TestClass]
public sealed class AuditingApiMySqlTests
{
    [TestMethod]
    public async Task Host_log_timeline_correlates_all_sources_with_mysql()
    {
        using var factory = new FullNetApiFactory(DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
        await AuditingLogTimelineAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Host_access_log_query_follows_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            new Dictionary<string, string?>
            {
                ["Auditing:AccessLogCapture:Enabled"] = "true",
            });

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

    [TestMethod]
    public async Task Request_audit_blocked_batch_times_out_and_recovers_with_mysql()
    {
        using var factory = new FullNetApiFactory(DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
        await AuditingBatchFaultAssertions.VerifyAsync(factory, AuditingBatchFaultAssertions.Failure.BatchDeadline);
    }

    [TestMethod]
    public async Task Request_audit_blocked_batch_cancels_and_recovers_with_mysql()
    {
        using var factory = new FullNetApiFactory(DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
        await AuditingBatchFaultAssertions.VerifyAsync(factory, AuditingBatchFaultAssertions.Failure.CallerCancellation);
    }

    [TestMethod]
    public async Task Request_audit_disconnected_batch_fails_once_and_recovers_with_mysql()
    {
        using var factory = new FullNetApiFactory(DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
        await AuditingBatchFaultAssertions.VerifyAsync(factory, AuditingBatchFaultAssertions.Failure.ConnectionLost);
    }

    [TestMethod]
    public async Task Audit_retention_deletes_expired_rows_in_fair_batches_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await AuditingRetentionAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Host_outbound_call_log_query_follows_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await AuditingOutboundCallAssertions.VerifyAsync(factory);
    }
}
