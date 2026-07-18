using Full.NET.Abstractions.Results;
using Full.NET.Host.Migrator;
using Full.NET.Migrations.DbUp;
using Full.NET.Seeding.Abstractions;
using NSubstitute;

namespace Full.NET.UnitTests.Hosting;

[TestClass]
public sealed class MigratorWorkflowTests
{
    [TestMethod]
    public async Task Missing_seed_argument_runs_migration_only()
    {
        var (workflow, migration, orchestrator) = CreateWorkflow();

        var result = await workflow.RunAsync([]);

        Assert.AreEqual(3, result.ExecutedScriptCount);
        Assert.IsNull(result.SeedProfile);
        Assert.IsFalse(result.UsesLegacyAlias);
        await migration.Received(1).MigrateAsync(Arg.Any<CancellationToken>());
        await orchestrator.DidNotReceive().RunAsync(
            Arg.Any<SeedProfile>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("baseline", SeedProfile.Baseline)]
    [DataRow("development", SeedProfile.Development)]
    public async Task Explicit_seed_runs_selected_profile_after_migration(
        string profileName,
        SeedProfile expectedProfile)
    {
        var (workflow, migration, orchestrator) = CreateWorkflow();

        var result = await workflow.RunAsync(["--seed", profileName]);

        Assert.AreEqual(expectedProfile, result.SeedProfile);
        Received.InOrder(() =>
        {
            migration.MigrateAsync(Arg.Any<CancellationToken>());
            orchestrator.RunAsync(expectedProfile, Arg.Any<CancellationToken>());
        });
    }

    [TestMethod]
    public async Task Legacy_alias_runs_development_with_deprecation_flag()
    {
        var (workflow, _, orchestrator) = CreateWorkflow();

        var result = await workflow.RunAsync(["--seed-local"]);

        Assert.AreEqual(SeedProfile.Development, result.SeedProfile);
        Assert.IsTrue(result.UsesLegacyAlias);
        await orchestrator.Received(1).RunAsync(
            SeedProfile.Development,
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Migration_failure_prevents_seed_execution()
    {
        var migration = Substitute.For<IDatabaseMigrationRunner>();
        migration.MigrateAsync(Arg.Any<CancellationToken>())
            .Returns<Task<MigrationResult>>(_ => throw new InvalidOperationException("database details"));
        var orchestrator = Substitute.For<ISeedOrchestrator>();
        var workflow = new MigratorWorkflow(migration, orchestrator);

        var exception = await Assert.ThrowsExactlyAsync<MigratorWorkflowException>(
            () => workflow.RunAsync(["--seed", "baseline"]));

        Assert.AreEqual(MigratorErrorCodes.MigrationFailed, exception.Code);
        await orchestrator.DidNotReceive().RunAsync(
            Arg.Any<SeedProfile>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Seed_failure_causes_workflow_failure_with_stable_code()
    {
        const string errorCode = "seeding.profile.not_allowed";
        var (workflow, _, orchestrator) = CreateWorkflow();
        orchestrator.RunAsync(
                SeedProfile.Baseline,
                Arg.Any<CancellationToken>())
            .Returns(Result<SeedRunResult>.Failure(new Error(
                errorCode,
                errorCode,
                ErrorType.Forbidden)));

        var exception = await Assert.ThrowsExactlyAsync<MigratorWorkflowException>(
            () => workflow.RunAsync(["--seed", "baseline"]));

        Assert.AreEqual(errorCode, exception.Code);
    }

    [TestMethod]
    public async Task Cancellation_token_is_propagated_to_both_stages()
    {
        using var cancellation = new CancellationTokenSource();
        var (workflow, migration, orchestrator) = CreateWorkflow();

        await workflow.RunAsync(["--seed", "development"], cancellation.Token);

        await migration.Received(1).MigrateAsync(cancellation.Token);
        await orchestrator.Received(1).RunAsync(
            SeedProfile.Development,
            cancellation.Token);
    }

    private static (
        MigratorWorkflow Workflow,
        IDatabaseMigrationRunner Migration,
        ISeedOrchestrator Orchestrator) CreateWorkflow()
    {
        var migration = Substitute.For<IDatabaseMigrationRunner>();
        migration.MigrateAsync(Arg.Any<CancellationToken>())
            .Returns(new MigrationResult(true, 3));
        var orchestrator = Substitute.For<ISeedOrchestrator>();
        orchestrator.RunAsync(
                Arg.Any<SeedProfile>(),
                Arg.Any<CancellationToken>())
            .Returns(call => Result<SeedRunResult>.Success(new SeedRunResult(
                Guid.CreateVersion7(),
                call.ArgAt<SeedProfile>(0),
                1,
                1,
                0,
                0)));
        return (new MigratorWorkflow(migration, orchestrator), migration, orchestrator);
    }
}
