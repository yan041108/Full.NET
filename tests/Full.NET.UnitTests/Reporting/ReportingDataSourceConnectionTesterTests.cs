using Full.NET.Data.Abstractions;
using Full.NET.Modules.Reporting.Connectivity;
using Full.NET.Modules.Reporting.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Reporting;

[TestClass]
public sealed class ReportingDataSourceConnectionTesterTests
{
    [TestMethod]
    [DataRow("sql_server", "open-null")]
    [DataRow("mysql", "open-null")]
    [DataRow("sql_server", "open-failed-null")]
    [DataRow("mysql", "open-failed-null")]
    [DataRow("sql_server", "open-failed-session")]
    [DataRow("mysql", "open-failed-session")]
    [DataRow("sql_server", "open-succeeded-null")]
    [DataRow("mysql", "open-succeeded-null")]
    [DataRow("sql_server", "probe-null")]
    [DataRow("mysql", "probe-null")]
    [DataRow("sql_server", "probe-failed")]
    [DataRow("mysql", "probe-failed")]
    [DataRow("sql_server", "probe-empty")]
    [DataRow("mysql", "probe-empty")]
    [DataRow("sql_server", "probe-throws")]
    [DataRow("mysql", "probe-throws")]
    [DataRow("sql_server", "success")]
    [DataRow("mysql", "success")]
    public async Task Probe_returns_stable_summary_and_releases_every_owned_session(string provider, string kind)
    {
        var fixture = new ProbeFixture(provider, kind);
        if (kind == "probe-throws")
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Test(default));
        }
        else
        {
            var outcome = await fixture.Test(default);
            Assert.AreEqual(kind == "success", outcome.Succeeded);
            Assert.IsFalse(outcome.Message.Contains(ProbeFixture.PrivateDriverMessage, StringComparison.Ordinal));
        }
        var ownsSession = kind == "open-failed-session" || !kind.StartsWith("open-", StringComparison.Ordinal);
        Assert.AreEqual(ownsSession ? 1 : 0, fixture.DisposeCalls);
        Assert.AreEqual(kind.StartsWith("open-", StringComparison.Ordinal) ? 0 : 1, fixture.ProbeCalls);
        Assert.AreEqual(1, fixture.OpenCalls);
    }

    [TestMethod]
    [DataRow("sql_server", "entry")]
    [DataRow("mysql", "entry")]
    [DataRow("sql_server", "open")]
    [DataRow("mysql", "open")]
    [DataRow("sql_server", "open-null")]
    [DataRow("mysql", "open-null")]
    [DataRow("sql_server", "probe")]
    [DataRow("mysql", "probe")]
    [DataRow("sql_server", "probe-null")]
    [DataRow("mysql", "probe-null")]
    public async Task Ignored_cancellation_never_becomes_a_probe_success_or_ordinary_failure(string provider, string stage)
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new ProbeFixture(provider, stage, cancellation.Cancel);
        if (stage == "entry") cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Test(cancellation.Token));
        Assert.AreEqual(stage is "entry" or "open-null" ? 0 : 1, fixture.DisposeCalls);
        Assert.AreEqual(stage.StartsWith("probe", StringComparison.Ordinal) ? 1 : 0, fixture.ProbeCalls);
        Assert.AreEqual(stage == "entry" ? 0 : 1, fixture.OpenCalls);
    }

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

    private sealed class ProbeFixture
    {
        public const string PrivateDriverMessage = "Password=fixture;Server=private-driver-endpoint";
        private readonly ReportingDataSourceConnectionTester tester;
        private readonly ReportingDataSourceRecord record;
        public int OpenCalls { get; private set; }
        public int ProbeCalls { get; private set; }
        public int DisposeCalls { get; private set; }

        public ProbeFixture(string provider, string kind, Action? cancel = null)
        {
            record = new ReportingDataSourceRecord
            {
                ProviderKey = provider, ServerHost = "example.invalid", Port = provider == "sql_server" ? 1433 : 3306,
                DatabaseName = "metadata", Username = "readonly",
            };
            var session = Substitute.For<IExternalDatabaseSession>();
            session.DisposeAsync().Returns(_ => { DisposeCalls++; return ValueTask.CompletedTask; });
            session.ExecuteScalarAsync(Arg.Is("SELECT 1"), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(_ =>
            {
                ProbeCalls++;
                if (kind.StartsWith("probe", StringComparison.Ordinal)) cancel?.Invoke();
                if (kind == "probe-throws") throw new InvalidOperationException("fixture probe failure");
                if (kind == "probe-null") return null!;
                return new ExternalDatabaseScalarResult(kind != "probe-failed", PrivateDriverMessage, kind == "probe-empty" ? null : 1);
            });
            var factory = Substitute.For<IExternalDatabaseConnectionFactory>();
            factory.OpenAsync(Arg.Any<ExternalDatabaseConnectionRequest>(), Arg.Any<CancellationToken>()).Returns(_ =>
            {
                OpenCalls++;
                if (kind is "open" or "open-null") cancel?.Invoke();
                if (kind == "open-null") return null!;
                var handedOff = kind is "open-failed-null" or "open-succeeded-null" ? null : session;
                return new ExternalDatabaseSessionResult(!kind.StartsWith("open-failed", StringComparison.Ordinal), PrivateDriverMessage, handedOff);
            });
            tester = new(factory);
        }

        public Task<(bool Succeeded, string Message)> Test(CancellationToken token) => tester.TestAsync(record, "fixture", token);
    }
}
