using Full.NET.Data.Abstractions;
using Full.NET.Modules.Reporting.Connectivity;
using Full.NET.Modules.Reporting.Persistence;

namespace Full.NET.UnitTests.Reporting;

[TestClass]
public sealed class ReportingDataSourceConnectionTesterTests
{
    [TestMethod]
    public async Task Driver_error_must_not_be_returned_as_test_summary()
    {
        const string secret = "Password=private-value;Server=internal-db";
        var tester = new ReportingDataSourceConnectionTester(new FailingConnectionFactory(secret));
        var record = new ReportingDataSourceRecord
        {
            ProviderKey = "sql_server",
            ServerHost = "db.example.com",
            Port = 1433,
            DatabaseName = "reports",
            Username = "reader",
        };

        var outcome = await tester.TestAsync(record, "password");

        Assert.IsFalse(outcome.Succeeded);
        Assert.IsFalse(outcome.Message.Contains(secret, StringComparison.Ordinal));
    }

    private sealed class FailingConnectionFactory(string errorMessage) : IExternalDatabaseConnectionFactory
    {
        public Task<ExternalDatabaseSessionResult> OpenAsync(
            ExternalDatabaseConnectionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExternalDatabaseSessionResult(false, errorMessage, null));
    }
}
