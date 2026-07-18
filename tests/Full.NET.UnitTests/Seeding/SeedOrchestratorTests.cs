using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Seeding.Abstractions;
using Full.NET.Seeding.Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Seeding;

[TestClass]
public sealed class SeedOrchestratorTests
{
    private static readonly DateTimeOffset StartedAtUtc =
        new(2026, 7, 18, 8, 0, 0, TimeSpan.Zero);

    [TestMethod]
    [DataRow(SeedProfile.Development)]
    [DataRow(SeedProfile.Demo)]
    [DataRow(SeedProfile.Test)]
    public async Task Production_rejects_overlay_before_acquiring_lease(SeedProfile profile)
    {
        var leaseProvider = Substitute.For<ISeedExecutionLeaseProvider>();
        var store = Substitute.For<ISeedExecutionStore>();
        var orchestrator = CreateOrchestrator([], leaseProvider, store, "Production");

        var result = await orchestrator.RunAsync(profile);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(SeedErrorCodes.ProfileNotAllowed, result.Error!.Code);
        await leaseProvider.DidNotReceive().AcquireAsync(Arg.Any<CancellationToken>());
        await store.DidNotReceive().StartRunAsync(
            Arg.Any<SeedRunAuditStart>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Production_allows_baseline_and_acquires_one_lease()
    {
        var leaseProvider = Substitute.For<ISeedExecutionLeaseProvider>();
        var store = Substitute.For<ISeedExecutionStore>();
        var lease = Substitute.For<IAsyncDisposable>();
        leaseProvider.AcquireAsync(Arg.Any<CancellationToken>()).Returns(lease);
        var orchestrator = CreateOrchestrator([], leaseProvider, store, "Production");

        var result = await orchestrator.RunAsync(SeedProfile.Baseline);

        Assert.IsTrue(result.IsSuccess);
        await leaseProvider.Received(1).AcquireAsync(Arg.Any<CancellationToken>());
        await lease.Received(1).DisposeAsync();
    }

    [TestMethod]
    public async Task Successful_run_records_audit_and_aggregates_contributor_counts()
    {
        var leaseProvider = Substitute.For<ISeedExecutionLeaseProvider>();
        var store = Substitute.For<ISeedExecutionStore>();
        var lease = Substitute.For<IAsyncDisposable>();
        leaseProvider.AcquireAsync(Arg.Any<CancellationToken>()).Returns(lease);
        var contributors = new IDataSeedContributor[]
        {
            Contributor("tenancy.host", new SeedContributionResult(1, 2, 3, "seed.succeeded")),
            Contributor(
                "identity.authorization",
                new SeedContributionResult(4, 5, 6, "seed.succeeded"),
                "tenancy.host"),
        };
        var orchestrator = CreateOrchestrator(
            contributors,
            leaseProvider,
            store,
            "Development");

        var result = await orchestrator.RunAsync(SeedProfile.Baseline);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(5, result.Value!.CreatedCount);
        Assert.AreEqual(7, result.Value.UpdatedCount);
        Assert.AreEqual(9, result.Value.SkippedCount);
        await store.Received(1).StartRunAsync(
            Arg.Is<SeedRunAuditStart>(audit =>
                audit != null &&
                audit.Profile == SeedProfile.Baseline.ToCanonicalName() &&
                audit.EnvironmentName == "Development"),
            Arg.Any<CancellationToken>());
        await store.Received(2).StartItemAsync(
            Arg.Any<SeedRunItemAuditStart>(),
            Arg.Any<CancellationToken>());
        await store.Received(2).CompleteItemAsync(
            Arg.Is<SeedRunItemAuditCompletion>(audit =>
                audit != null &&
                audit.Status == SeedExecutionStatuses.Succeeded &&
                audit.ErrorCode == null),
            Arg.Any<CancellationToken>());
        await store.Received(1).CompleteRunAsync(
            Arg.Is<SeedRunAuditCompletion>(audit =>
                audit != null &&
                audit.Status == SeedExecutionStatuses.Succeeded &&
                audit.ErrorCode == null),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Contributor_failure_records_stable_code_and_stops_later_contributors()
    {
        const string sensitiveMessage = "secret-input-must-not-be-audited";
        var failure = new InvalidOperationException(sensitiveMessage);
        var leaseProvider = Substitute.For<ISeedExecutionLeaseProvider>();
        var store = Substitute.For<ISeedExecutionStore>();
        var logger = Substitute.For<ILogger<SeedOrchestrator>>();
        leaseProvider.AcquireAsync(Arg.Any<CancellationToken>())
            .Returns(Substitute.For<IAsyncDisposable>());
        var later = Contributor(
            "tenancy.later",
            new SeedContributionResult(1, 0, 0, "seed.succeeded"));
        var contributors = new IDataSeedContributor[]
        {
            Contributor("tenancy.failure", _ => throw failure),
            later,
        };
        var orchestrator = CreateOrchestrator(
            contributors,
            leaseProvider,
            store,
            "Development",
            logger);

        var result = await orchestrator.RunAsync(SeedProfile.Baseline);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(SeedErrorCodes.ContributorFailed, result.Error!.Code);
        Assert.AreEqual(0, ((StubContributor)later).InvocationCount);
        await store.Received(1).CompleteItemAsync(
            Arg.Is<SeedRunItemAuditCompletion>(audit =>
                audit != null &&
                audit.Status == SeedExecutionStatuses.Failed &&
                audit.ErrorCode == SeedErrorCodes.ContributorFailed),
            Arg.Any<CancellationToken>());
        await store.Received(1).CompleteRunAsync(
            Arg.Is<SeedRunAuditCompletion>(audit =>
                audit != null &&
                audit.Status == SeedExecutionStatuses.Failed &&
                audit.ErrorCode == SeedErrorCodes.ContributorFailed),
            Arg.Any<CancellationToken>());
        Assert.IsFalse(store.ReceivedCalls()
            .SelectMany(call => call.GetArguments())
            .OfType<string>()
            .Any(value => value.Contains(sensitiveMessage, StringComparison.Ordinal)));
        Assert.IsTrue(logger.ReceivedCalls()
            .SelectMany(call => call.GetArguments())
            .Any(argument => ReferenceEquals(argument, failure)));
    }

    [TestMethod]
    public async Task Contributor_stable_failure_code_is_preserved_in_result_and_audit()
    {
        var leaseProvider = Substitute.For<ISeedExecutionLeaseProvider>();
        var store = Substitute.For<ISeedExecutionStore>();
        leaseProvider.AcquireAsync(Arg.Any<CancellationToken>())
            .Returns(Substitute.For<IAsyncDisposable>());
        var contributor = Contributor(
            "tenancy.conflict",
            _ => throw new SeedContributionException(
                SeedContributionErrorCodes.DataConflict));
        var orchestrator = CreateOrchestrator(
            [contributor],
            leaseProvider,
            store,
            "Development");

        var result = await orchestrator.RunAsync(SeedProfile.Baseline);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(SeedContributionErrorCodes.DataConflict, result.Error!.Code);
        await store.Received(1).CompleteItemAsync(
            Arg.Is<SeedRunItemAuditCompletion>(audit =>
                audit != null &&
                audit.Status == SeedExecutionStatuses.Failed &&
                audit.ErrorCode == SeedContributionErrorCodes.DataConflict),
            Arg.Any<CancellationToken>());
        await store.Received(1).CompleteRunAsync(
            Arg.Is<SeedRunAuditCompletion>(audit =>
                audit != null &&
                audit.Status == SeedExecutionStatuses.Failed &&
                audit.ErrorCode == SeedContributionErrorCodes.DataConflict),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Cancellation_records_cancelled_and_returns_stable_code()
    {
        var cancellation = new CancellationTokenSource();
        var leaseProvider = Substitute.For<ISeedExecutionLeaseProvider>();
        var store = Substitute.For<ISeedExecutionStore>();
        leaseProvider.AcquireAsync(Arg.Any<CancellationToken>())
            .Returns(Substitute.For<IAsyncDisposable>());
        var contributor = Contributor(
            "tenancy.cancel",
            _ =>
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            });
        var orchestrator = CreateOrchestrator(
            [contributor],
            leaseProvider,
            store,
            "Test");

        var result = await orchestrator.RunAsync(
            SeedProfile.Baseline,
            cancellation.Token);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(SeedErrorCodes.ExecutionCancelled, result.Error!.Code);
        await store.Received(1).CompleteRunAsync(
            Arg.Is<SeedRunAuditCompletion>(audit =>
                audit != null &&
                audit.Status == SeedExecutionStatuses.Cancelled &&
                audit.ErrorCode == SeedErrorCodes.ExecutionCancelled),
            CancellationToken.None);
    }

    private static SeedOrchestrator CreateOrchestrator(
        IEnumerable<IDataSeedContributor> contributors,
        ISeedExecutionLeaseProvider leaseProvider,
        ISeedExecutionStore store,
        string environmentName,
        ILogger<SeedOrchestrator>? logger = null)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);

        return new SeedOrchestrator(
            contributors,
            leaseProvider,
            store,
            environment,
            Options.Create(new SeedOptions()),
            new StubClock(StartedAtUtc),
            new StubIdGenerator(Guid.Parse("019822d3-0700-7000-8000-000000000001")),
            logger ?? NullLogger<SeedOrchestrator>.Instance);
    }

    private static StubContributor Contributor(
        string name,
        SeedContributionResult result,
        params string[] dependencies) =>
        new(name, _ => Task.FromResult(result), dependencies);

    private static StubContributor Contributor(
        string name,
        Func<SeedContext, Task<SeedContributionResult>> callback,
        params string[] dependencies) =>
        new(name, callback, dependencies);

    private sealed class StubContributor(
        string name,
        Func<SeedContext, Task<SeedContributionResult>> callback,
        IReadOnlyCollection<string> dependencies) : IDataSeedContributor
    {
        public string Name { get; } = name;

        public int Version => 1;

        public IReadOnlySet<SeedProfile> Profiles { get; } =
            new HashSet<SeedProfile> { SeedProfile.Baseline };

        public IReadOnlyCollection<string> Dependencies { get; } = dependencies;

        public int InvocationCount { get; private set; }

        public Task<SeedContributionResult> SeedAsync(
            SeedContext context,
            CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            return callback(context);
        }
    }

    private sealed class StubClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }

    private sealed class StubIdGenerator(Guid id) : IIdGenerator
    {
        public Guid NewId() => id;
    }
}
