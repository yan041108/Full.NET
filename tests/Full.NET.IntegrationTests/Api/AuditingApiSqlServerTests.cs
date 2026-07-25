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
}
