using Full.NET.Abstractions.Results;
using Full.NET.Composition;
using Full.NET.Hosting.Migrator;
using Full.NET.Migrations.DbUp;
using Full.NET.Seeding.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Full.NET.UnitTests.Hosting;

[TestClass]
[DoNotParallelize]
public sealed class FullNetMigratorHostTests
{
    [TestMethod]
    public async Task Null_arguments_are_rejected_before_creating_or_building_host()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => FullNetMigratorHost.CreateBuilder(null!));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => FullNetMigratorHost.RunAsync(null!, []));
        var fixture = CreateFixture();
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => FullNetMigratorHost.RunAsync(fixture.Builder, null!));
        Assert.IsFalse(fixture.Lifecycle.Started);
    }

    [TestMethod]
    public async Task Missing_seed_runs_only_migration_and_releases_host()
    {
        var fixture = CreateFixture();
        Assert.AreEqual(0, await FullNetMigratorHost.RunAsync(fixture.Builder, []));
        await fixture.Migration.Received(1).MigrateAsync(Arg.Any<CancellationToken>());
        await fixture.Seed.DidNotReceive().RunAsync(Arg.Any<SeedProfile>(), Arg.Any<CancellationToken>());
        Assert.IsTrue(fixture.Lifecycle.Started && fixture.Lifecycle.Stopped && fixture.Lifecycle.Disposed);
    }

    [TestMethod]
    [DataRow("baseline", SeedProfile.Baseline)]
    [DataRow("development", SeedProfile.Development)]
    public async Task Selected_seed_runs_after_migration(string name, SeedProfile profile)
    {
        var fixture = CreateFixture();
        Assert.AreEqual(0, await FullNetMigratorHost.RunAsync(fixture.Builder, ["--seed", name]));
        Received.InOrder(() =>
        {
            fixture.Migration.MigrateAsync(Arg.Any<CancellationToken>());
            fixture.Seed.RunAsync(profile, Arg.Any<CancellationToken>());
        });
        Assert.IsTrue(fixture.Lifecycle.Stopped && fixture.Lifecycle.Disposed);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Migration_failure_blocks_seed_and_releases_host(bool throws)
    {
        var fixture = CreateFixture();
        if (throws) fixture.Migration.MigrateAsync(Arg.Any<CancellationToken>())
            .Returns<Task<MigrationResult>>(_ => throw new InvalidOperationException("fixture failure"));
        else fixture.Migration.MigrateAsync(Arg.Any<CancellationToken>()).Returns(new MigrationResult(false, 0));
        Assert.AreEqual(1, await FullNetMigratorHost.RunAsync(fixture.Builder, ["--seed", "baseline"]));
        await fixture.Seed.DidNotReceive().RunAsync(Arg.Any<SeedProfile>(), Arg.Any<CancellationToken>());
        Assert.IsTrue(fixture.Lifecycle.Stopped && fixture.Lifecycle.Disposed);
    }

    [TestMethod]
    public async Task Invalid_seed_blocks_migration_and_releases_host()
    {
        var fixture = CreateFixture();
        Assert.AreEqual(1, await FullNetMigratorHost.RunAsync(fixture.Builder, ["--seed", "unknown"]));
        await fixture.Migration.DidNotReceive().MigrateAsync(Arg.Any<CancellationToken>());
        Assert.IsTrue(fixture.Lifecycle.Stopped && fixture.Lifecycle.Disposed);
    }

    [TestMethod]
    public async Task Precancelled_caller_blocks_all_writes()
    {
        var fixture = CreateFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.AreEqual(1, await FullNetMigratorHost.RunAsync(fixture.Builder, ["--seed", "baseline"], cancellation.Token));
        await fixture.Migration.DidNotReceive().MigrateAsync(Arg.Any<CancellationToken>());
        await fixture.Seed.DidNotReceive().RunAsync(Arg.Any<SeedProfile>(), Arg.Any<CancellationToken>());
        Assert.IsFalse(fixture.Lifecycle.Started);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Caller_cancellation_during_migration_reaches_workflow_and_blocks_seed(bool cooperative)
    {
        var fixture = CreateFixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Migration.MigrateAsync(Arg.Any<CancellationToken>()).Returns(call =>
        {
            cancellation.Cancel();
            if (cooperative) call.Arg<CancellationToken>().ThrowIfCancellationRequested();
            return new MigrationResult(true, 3);
        });
        Assert.AreEqual(1, await FullNetMigratorHost.RunAsync(fixture.Builder, ["--seed", "baseline"], cancellation.Token));
        await fixture.Seed.DidNotReceive().RunAsync(Arg.Any<SeedProfile>(), Arg.Any<CancellationToken>());
        Assert.IsTrue(fixture.Lifecycle.Stopped && fixture.Lifecycle.Disposed);
    }

    [TestMethod]
    public async Task Seed_failure_returns_failure_and_releases_host()
    {
        var fixture = CreateFixture();
        fixture.Seed.RunAsync(SeedProfile.Baseline, Arg.Any<CancellationToken>()).Returns(
            Result<SeedRunResult>.Failure(new Error("seeding.profile.not_allowed", "fixture", ErrorType.Forbidden)));
        Assert.AreEqual(1, await FullNetMigratorHost.RunAsync(fixture.Builder, ["--seed", "baseline"]));
        Assert.IsTrue(fixture.Lifecycle.Stopped && fixture.Lifecycle.Disposed);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Shutdown_failure_returns_failure_without_overwriting_workflow_error(bool migrationFails)
    {
        var fixture = CreateFixture();
        fixture.Lifecycle.FailStopping = true;
        fixture.Migration.MigrateAsync(Arg.Any<CancellationToken>()).Returns(new MigrationResult(!migrationFails, 0));
        var originalError = Console.Error;
        using var error = new StringWriter();
        try
        {
            Console.SetError(error);
            Assert.AreEqual(1, await FullNetMigratorHost.RunAsync(fixture.Builder, []));
        }
        finally
        {
            Console.SetError(originalError);
        }

        Assert.AreEqual(migrationFails ? MigratorErrorCodes.MigrationFailed : MigratorErrorCodes.ExecutionFailed,
            error.ToString().Trim());
        Assert.IsTrue(fixture.Lifecycle.Stopped && fixture.Lifecycle.Disposed);
    }

    [TestMethod]
    public async Task Startup_failure_stops_started_services_without_running_migration()
    {
        var fixture = CreateFixture();
        fixture.Lifecycle.FailStarting = true;
        Assert.AreEqual(1, await FullNetMigratorHost.RunAsync(fixture.Builder, []));
        await fixture.Migration.DidNotReceive().MigrateAsync(Arg.Any<CancellationToken>());
        Assert.IsTrue(fixture.Lifecycle.Started && fixture.Lifecycle.Stopped && fixture.Lifecycle.Disposed);
    }

    private static (HostApplicationBuilder Builder, IDatabaseMigrationRunner Migration,
        ISeedOrchestrator Seed, LifecycleProbe Lifecycle) CreateFixture()
    {
        // 使用真实宿主装配并替换写入端口；不可连接配置不证明数据库运行。
        var builder = FullNetMigratorHost.CreateBuilder(["--environment", "Development"]);
        // 隔离后续模块配置绑定，避免依赖输出目录残留的Development签名配置。
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "mysql",
            ["Database:ConnectionString"] = "Server=127.0.0.1;Port=1;Database=migrator_probe;User ID=probe;Password=fixture;",
            ["Database:MySqlGuidStorageMode"] = "Binary16",
            ["FullNet:Modules:Preset"] = "minimal",
            ["Identity:AllowDevelopmentEphemeralSigningKey"] = "true",
        });
        builder.Services.AddFullNetApplicationModules(builder.Configuration, FullNetHostProfile.Migrator);
        var migration = Substitute.For<IDatabaseMigrationRunner>();
        migration.MigrateAsync(Arg.Any<CancellationToken>()).Returns(new MigrationResult(true, 3));
        var seed = Substitute.For<ISeedOrchestrator>();
        seed.RunAsync(Arg.Any<SeedProfile>(), Arg.Any<CancellationToken>()).Returns(call =>
            Result<SeedRunResult>.Success(new SeedRunResult(Guid.CreateVersion7(), call.ArgAt<SeedProfile>(0), 1, 1, 0, 0)));
        builder.Services.Replace(ServiceDescriptor.Singleton(migration));
        builder.Services.Replace(ServiceDescriptor.Scoped<ISeedOrchestrator>(_ => seed));
        var lifecycle = new LifecycleProbe();
        builder.Services.AddSingleton<IHostedService>(_ => lifecycle);
        return (builder, migration, seed, lifecycle);
    }

    private sealed class LifecycleProbe : IHostedService, IDisposable
    {
        public bool Started { get; private set; }
        public bool Stopped { get; private set; }
        public bool Disposed { get; private set; }
        public bool FailStopping { get; set; }
        public bool FailStarting { get; set; }
        public Task StartAsync(CancellationToken cancellationToken)
        {
            Started = true;
            if (FailStarting) throw new InvalidOperationException("fixture startup failure");
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Stopped = true;
            if (FailStopping) throw new InvalidOperationException("fixture shutdown failure");
            return Task.CompletedTask;
        }
        public void Dispose() => Disposed = true;
    }
}
