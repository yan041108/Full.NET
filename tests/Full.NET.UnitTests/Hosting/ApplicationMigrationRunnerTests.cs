using Demo.Host.Migrator;
using Full.NET.Data.Abstractions;
using Full.NET.Migrations.DbUp;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Hosting;

[TestClass]
public sealed class ApplicationMigrationRunnerTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Precancelled_migration_never_calls_framework(DatabaseProvider provider)
    {
        var framework = new FrameworkRunner(() => new MigrationResult(true, 2));
        var runner = Create(provider, framework);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => runner.MigrateAsync(new CancellationToken(true)));
        Assert.AreEqual(0, framework.Calls);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Framework_failure_stops_before_application_database(DatabaseProvider provider)
    {
        var framework = new FrameworkRunner(() => new MigrationResult(false, 0));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Create(provider, framework).MigrateAsync());
        Assert.AreEqual(1, framework.Calls);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Cancellation_after_framework_stops_before_application_database(DatabaseProvider provider)
    {
        using var cancellation = new CancellationTokenSource();
        var framework = new FrameworkRunner(() => { cancellation.Cancel(); return new MigrationResult(true, 2); });
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => Create(provider, framework).MigrateAsync(cancellation.Token));
        Assert.AreEqual(1, framework.Calls);
    }

    private static ApplicationMigrationRunner Create(DatabaseProvider provider, IDatabaseMigrationRunner framework) =>
        new(framework, Options.Create(new DatabaseOptions { Provider = provider, ConnectionString = "invalid-connection-must-not-be-used" }), "SELECT 1;", "SELECT 1;", null, null);

    // 故意忽略令牌，要求包装器自行阻止取消后的业务迁移。
    private sealed class FrameworkRunner(Func<MigrationResult> action) : IDatabaseMigrationRunner
    {
        public int Calls { get; private set; }
        public Task<MigrationResult> MigrateAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(action());
        }
    }
}
