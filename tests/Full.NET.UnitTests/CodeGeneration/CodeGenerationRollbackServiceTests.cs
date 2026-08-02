using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Modules.CodeGeneration.Configuration;
using Full.NET.Modules.CodeGeneration.Contracts;
using Full.NET.Modules.CodeGeneration.Features.ManageHostRuns;
using Full.NET.Modules.CodeGeneration.Features.NormalizeCrudSchema;
using Full.NET.Modules.CodeGeneration.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class CodeGenerationRollbackServiceTests
{
    private static readonly Guid ActorUserId = Guid.Parse(
        "0198f36e-f7a7-7c52-9cbb-774e67411311");
    private static readonly Guid ApplyRunId = Guid.Parse(
        "0198f36e-f7a7-7c52-9cbb-774e67411312");
    private static readonly Guid ApplyRunId2 = Guid.Parse(
        "0198f36e-f7a7-7c52-9cbb-774e67411315");
    private static readonly Guid RollbackRunId = Guid.Parse(
        "0198f36e-f7a7-7c52-9cbb-774e67411313");
    private static readonly Guid RollbackRunId2 = Guid.Parse(
        "0198f36e-f7a7-7c52-9cbb-774e67411316");
    private static readonly DateTimeOffset Now = new(
        2026,
        8,
        2,
        8,
        0,
        0,
        TimeSpan.Zero);

    [TestMethod]
    public async Task Succeeded_apply_restores_workspace_and_persists_rollback_summary()
    {
        using var workspace = new TemporaryDirectory();
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(
                CodeGenerationRunSql.Insert,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        command.ExecuteAsync(
                CodeGenerationRunSql.CompleteRollback,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        var fixture = await CreatePreparedFixtureAsync(workspace.Path, command);

        var result = await fixture.Service.RollbackAsync(
            ActorUserId,
            new CodeGenerationRunRollbackRequest(ApplyRunId));

        Assert.IsTrue(result.IsSuccess, result.Error?.Code);
        Assert.AreEqual(RollbackRunId, result.Value!.RunId);
        Assert.AreEqual(ApplyRunId, result.Value.ApplyRunId);
        Assert.AreEqual(0, result.Value.ArtifactCount);
        Assert.IsTrue(result.Value.ChangedArtifactCount > 0);
        Assert.AreEqual(
            CodeGenerationRunSummary.ComputeManifestSha256(
                GenerationManifest.Create([])),
            result.Value.ManifestSha256);
        Assert.AreEqual(
            GenerationManifest.Create([]).ToJson(),
            await File.ReadAllTextAsync(Path.Combine(
                workspace.Path,
                GenerationWorkspaceStore.ManifestRelativePath)));
        Assert.IsTrue(Directory.Exists(Path.Combine(
            workspace.Path,
            GenerationRollbackCheckpointStore.RootRelativePath,
            ApplyRunId.ToString("N"))));
        await command.Received(1).ExecuteAsync(
            CodeGenerationRunSql.Insert,
            Arg.Is<object>(parameters =>
                parameters != null
                && Read<string>(parameters, "OperationKind")
                    == CodeGenerationRunOperationKinds.Rollback
                && Read<string>(parameters, "Status")
                    == CodeGenerationRunStatuses.Running
                && Read<Guid?>(parameters, "SourceApplyRunId") == ApplyRunId
                && Read<Guid?>(parameters, "TemplateId") == null
                && Read<int>(parameters, "ArtifactCount") == 0
                && Read<string?>(parameters, "ErrorCode") == null),
            Arg.Any<CancellationToken>());
        await command.Received(1).ExecuteAsync(
            CodeGenerationRunSql.CompleteRollback,
            Arg.Is<object>(parameters =>
                parameters != null
                && Read<Guid>(parameters, "Id") == RollbackRunId),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Succeeded_rollback_deletes_checkpoint_when_configured()
    {
        using var workspace = new TemporaryDirectory();
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(
                CodeGenerationRunSql.Insert,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        command.ExecuteAsync(
                CodeGenerationRunSql.CompleteRollback,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        var fixture = await CreatePreparedFixtureAsync(
            workspace.Path,
            command,
            retention: new CodeGenerationCheckpointRetentionOptions
            {
                DeleteAfterSucceededRollback = true,
            });

        var result = await fixture.Service.RollbackAsync(
            ActorUserId,
            new CodeGenerationRunRollbackRequest(ApplyRunId));

        Assert.IsTrue(result.IsSuccess, result.Error?.Code);
        Assert.IsFalse(Directory.Exists(Path.Combine(
            workspace.Path,
            GenerationRollbackCheckpointStore.RootRelativePath,
            ApplyRunId.ToString("N"))));
    }

    [TestMethod]
    public async Task Rollback_chain_restores_workspace_in_lifo_order()
    {
        using var workspace = new TemporaryDirectory();
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(
                CodeGenerationRunSql.Insert,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        command.ExecuteAsync(
                CodeGenerationRunSql.CompleteRollback,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        var fixture = await CreateChainFixtureAsync(workspace.Path, command);
        var service = CreateService(
            workspace.Path,
            command,
            fixture.Query,
            idGenerator: new SequentialIdGenerator(RollbackRunId, RollbackRunId2));

        var result = await service.RollbackChainAsync(
            ActorUserId,
            new CodeGenerationRunRollbackChainRequest(
                [ApplyRunId2, ApplyRunId]));

        Assert.IsTrue(result.IsSuccess, result.Error?.Code);
        Assert.HasCount(2, result.Value!.Rollbacks);
        Assert.AreEqual(RollbackRunId, result.Value.Rollbacks[0].RunId);
        Assert.AreEqual(ApplyRunId2, result.Value.Rollbacks[0].ApplyRunId);
        Assert.AreEqual(RollbackRunId2, result.Value.Rollbacks[1].RunId);
        Assert.AreEqual(ApplyRunId, result.Value.Rollbacks[1].ApplyRunId);
        Assert.AreEqual(
            GenerationManifest.Create([]).ToJson(),
            await File.ReadAllTextAsync(Path.Combine(
                workspace.Path,
                GenerationWorkspaceStore.ManifestRelativePath)));
    }

    [TestMethod]
    public async Task Rollback_chain_rejects_out_of_order_apply_runs()
    {
        using var workspace = new TemporaryDirectory();
        var command = Substitute.For<ICommandExecutor>();
        var fixture = await CreateChainFixtureAsync(workspace.Path, command);
        var service = CreateService(
            workspace.Path,
            command,
            fixture.Query,
            idGenerator: new SequentialIdGenerator(RollbackRunId, RollbackRunId2));

        var result = await service.RollbackChainAsync(
            ActorUserId,
            new CodeGenerationRunRollbackChainRequest(
                [ApplyRunId, ApplyRunId2]));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            CodeGenerationRunErrorCodes.InvalidRollbackChain,
            result.Error!.Code);
        await command.DidNotReceive().ExecuteAsync(
            Arg.Any<SqlStatement>(),
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Rollback_chain_rejects_single_apply_run()
    {
        using var workspace = new TemporaryDirectory();
        var command = Substitute.For<ICommandExecutor>();
        var query = Substitute.For<IQueryExecutor>();
        var service = CreateService(workspace.Path, command, query);

        var result = await service.RollbackChainAsync(
            ActorUserId,
            new CodeGenerationRunRollbackChainRequest([ApplyRunId]));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            CodeGenerationRunErrorCodes.InvalidRollbackChain,
            result.Error!.Code);
    }

    [TestMethod]
    public async Task Invalid_apply_is_rejected_without_run_or_workspace_mutation()
    {
        using var workspace = new TemporaryDirectory();
        var command = Substitute.For<ICommandExecutor>();
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<CodeGenerationRunRecord>(
                CodeGenerationRunSql.FindById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(new CodeGenerationRunRecord
            {
                Id = ApplyRunId,
                OperationKind = CodeGenerationRunOperationKinds.Preview,
                Status = CodeGenerationRunStatuses.Succeeded,
                ModuleKey = "catalog",
                EntityKey = "product",
                SchemaSha256 = new string('a', 64),
                ArtifactCount = 1,
                ManifestSha256 = new string('b', 64),
                RequestedByUserId = ActorUserId,
                StartedAtUtc = Now,
                FinishedAtUtc = Now,
            });
        var service = CreateService(workspace.Path, command, query);

        var result = await service.RollbackAsync(
            ActorUserId,
            new CodeGenerationRunRollbackRequest(ApplyRunId));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            CodeGenerationRunErrorCodes.InvalidRollbackApply,
            result.Error!.Code);
        await command.DidNotReceive().ExecuteAsync(
            Arg.Any<SqlStatement>(),
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Succeeded_rollback_replays_idempotently_without_mutation()
    {
        using var workspace = new TemporaryDirectory();
        var emptyManifestSha = CodeGenerationRunSummary.ComputeManifestSha256(
            GenerationManifest.Create([]));
        Directory.CreateDirectory(Path.Combine(workspace.Path, ".fullnet"));
        await File.WriteAllTextAsync(
            Path.Combine(
                workspace.Path,
                GenerationWorkspaceStore.ManifestRelativePath),
            GenerationManifest.Create([]).ToJson());

        var command = Substitute.For<ICommandExecutor>();
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<CodeGenerationRunRecord>(
                CodeGenerationRunSql.FindById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateSucceededApplyRecord());
        query.QuerySingleOrDefaultAsync<CodeGenerationRunRecord>(
                CodeGenerationRunSql.FindRunningRollbackBySourceApplyRunId,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns((CodeGenerationRunRecord?)null);
        query.QuerySingleOrDefaultAsync<CodeGenerationRunRecord>(
                CodeGenerationRunSql.FindSucceededRollbackBySourceApplyRunId,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(new CodeGenerationRunRecord
            {
                Id = RollbackRunId,
                OperationKind = CodeGenerationRunOperationKinds.Rollback,
                Status = CodeGenerationRunStatuses.Succeeded,
                SourceApplyRunId = ApplyRunId,
                ModuleKey = "catalog",
                EntityKey = "product",
                SchemaSha256 = new string('a', 64),
                ArtifactCount = 0,
                ManifestSha256 = emptyManifestSha,
                RequestedByUserId = ActorUserId,
                StartedAtUtc = Now,
                FinishedAtUtc = Now,
            });
        var service = CreateService(workspace.Path, command, query);

        var result = await service.RollbackAsync(
            ActorUserId,
            new CodeGenerationRunRollbackRequest(ApplyRunId));

        Assert.IsTrue(result.IsSuccess, result.Error?.Code);
        Assert.AreEqual(RollbackRunId, result.Value!.RunId);
        Assert.AreEqual(ApplyRunId, result.Value.ApplyRunId);
        Assert.AreEqual(0, result.Value.ArtifactCount);
        Assert.AreEqual(0, result.Value.ChangedArtifactCount);
        Assert.AreEqual(emptyManifestSha, result.Value.ManifestSha256);
        await command.DidNotReceive().ExecuteAsync(
            Arg.Any<SqlStatement>(),
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Succeeded_rollback_with_drifted_workspace_returns_conflict()
    {
        using var workspace = new TemporaryDirectory();
        var command = Substitute.For<ICommandExecutor>();
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<CodeGenerationRunRecord>(
                CodeGenerationRunSql.FindById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateSucceededApplyRecord());
        query.QuerySingleOrDefaultAsync<CodeGenerationRunRecord>(
                CodeGenerationRunSql.FindRunningRollbackBySourceApplyRunId,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns((CodeGenerationRunRecord?)null);
        query.QuerySingleOrDefaultAsync<CodeGenerationRunRecord>(
                CodeGenerationRunSql.FindSucceededRollbackBySourceApplyRunId,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(new CodeGenerationRunRecord
            {
                Id = RollbackRunId,
                OperationKind = CodeGenerationRunOperationKinds.Rollback,
                Status = CodeGenerationRunStatuses.Succeeded,
                SourceApplyRunId = ApplyRunId,
                ModuleKey = "catalog",
                EntityKey = "product",
                SchemaSha256 = new string('a', 64),
                ArtifactCount = 0,
                ManifestSha256 = CodeGenerationRunSummary.ComputeManifestSha256(
                    GenerationManifest.Create([])),
                RequestedByUserId = ActorUserId,
                StartedAtUtc = Now,
                FinishedAtUtc = Now,
            });
        var fixture = await CreatePreparedFixtureAsync(workspace.Path, command, query);

        var result = await fixture.Service.RollbackAsync(
            ActorUserId,
            new CodeGenerationRunRollbackRequest(ApplyRunId));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            CodeGenerationRunErrorCodes.RollbackConflict,
            result.Error!.Code);
        await command.DidNotReceive().ExecuteAsync(
            Arg.Any<SqlStatement>(),
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Running_rollback_is_rejected_as_busy()
    {
        using var workspace = new TemporaryDirectory();
        var command = Substitute.For<ICommandExecutor>();
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<CodeGenerationRunRecord>(
                CodeGenerationRunSql.FindById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateSucceededApplyRecord());
        query.QuerySingleOrDefaultAsync<CodeGenerationRunRecord>(
                CodeGenerationRunSql.FindRunningRollbackBySourceApplyRunId,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(new CodeGenerationRunRecord
            {
                Id = RollbackRunId,
                OperationKind = CodeGenerationRunOperationKinds.Rollback,
                Status = CodeGenerationRunStatuses.Running,
                SourceApplyRunId = ApplyRunId,
                ModuleKey = "catalog",
                EntityKey = "product",
                SchemaSha256 = new string('a', 64),
                ArtifactCount = 0,
                ManifestSha256 = new string('c', 64),
                RequestedByUserId = ActorUserId,
                StartedAtUtc = Now,
                FinishedAtUtc = Now,
            });
        var service = CreateService(workspace.Path, command, query);

        var result = await service.RollbackAsync(
            ActorUserId,
            new CodeGenerationRunRollbackRequest(ApplyRunId));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            CodeGenerationRunErrorCodes.RollbackBusy,
            result.Error!.Code);
        await command.DidNotReceive().ExecuteAsync(
            Arg.Any<SqlStatement>(),
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Missing_checkpoint_is_rejected_before_run_insert()
    {
        using var workspace = new TemporaryDirectory();
        var command = Substitute.For<ICommandExecutor>();
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<CodeGenerationRunRecord>(
                CodeGenerationRunSql.FindById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateSucceededApplyRecord());
        query.QuerySingleOrDefaultAsync<CodeGenerationRunRecord>(
                CodeGenerationRunSql.FindSucceededRollbackBySourceApplyRunId,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns((CodeGenerationRunRecord?)null);
        query.QuerySingleOrDefaultAsync<CodeGenerationRunRecord>(
                CodeGenerationRunSql.FindRunningRollbackBySourceApplyRunId,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns((CodeGenerationRunRecord?)null);
        var service = CreateService(workspace.Path, command, query);

        var result = await service.RollbackAsync(
            ActorUserId,
            new CodeGenerationRunRollbackRequest(ApplyRunId));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            CodeGenerationRunErrorCodes.RollbackCheckpointMissing,
            result.Error!.Code);
        await command.DidNotReceive().ExecuteAsync(
            Arg.Any<SqlStatement>(),
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Concurrent_gate_is_rejected_before_run_or_workspace_mutation()
    {
        using var workspace = new TemporaryDirectory();
        var command = Substitute.For<ICommandExecutor>();
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<CodeGenerationRunRecord>(
                CodeGenerationRunSql.FindById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateSucceededApplyRecord());
        query.QuerySingleOrDefaultAsync<CodeGenerationRunRecord>(
                CodeGenerationRunSql.FindSucceededRollbackBySourceApplyRunId,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns((CodeGenerationRunRecord?)null);
        query.QuerySingleOrDefaultAsync<CodeGenerationRunRecord>(
                CodeGenerationRunSql.FindRunningRollbackBySourceApplyRunId,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns((CodeGenerationRunRecord?)null);
        var gate = CodeGenerationApplyGateTestSupport.CreateLocalGate(@"C:\workspaces\codegen");
        Assert.IsTrue(await gate.TryEnterAsync(CancellationToken.None));
        var service = CreateService(workspace.Path, command, query, gate);

        try
        {
            var result = await service.RollbackAsync(
                ActorUserId,
                new CodeGenerationRunRollbackRequest(ApplyRunId));

            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(
                CodeGenerationRunErrorCodes.RollbackBusy,
                result.Error!.Code);
            await command.DidNotReceive().ExecuteAsync(
                Arg.Any<SqlStatement>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            gate.Release();
        }
    }

    [TestMethod]
    public async Task Drifted_workspace_marks_running_attempt_failed_with_conflict()
    {
        using var workspace = new TemporaryDirectory();
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(
                CodeGenerationRunSql.Insert,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        command.ExecuteAsync(
                CodeGenerationRunSql.FailRollback,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        var fixture = await CreatePreparedFixtureAsync(workspace.Path, command);
        await File.WriteAllTextAsync(
            Path.Combine(workspace.Path, fixture.RelativePath),
            "user-drift\n");

        var result = await fixture.Service.RollbackAsync(
            ActorUserId,
            new CodeGenerationRunRollbackRequest(ApplyRunId));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            CodeGenerationRunErrorCodes.RollbackConflict,
            result.Error!.Code);
        Assert.AreEqual(
            "user-drift\n",
            await File.ReadAllTextAsync(
                Path.Combine(workspace.Path, fixture.RelativePath)));
        await command.Received(1).ExecuteAsync(
            CodeGenerationRunSql.FailRollback,
            Arg.Is<object>(parameters =>
                parameters != null
                && Read<string>(parameters, "ErrorCode")
                    == CodeGenerationRunErrorCodes.RollbackConflict),
            Arg.Any<CancellationToken>());
    }

    private static async Task<RollbackFixture> CreatePreparedFixtureAsync(
        string workspaceRoot,
        ICommandExecutor command,
        IQueryExecutor? queryOverride = null,
        CodeGenerationCheckpointRetentionOptions? retention = null)
    {
        const string relativePath = "Backend/Product.g.cs";
        const string appliedContent = "applied-product\n";
        var artifacts = new[]
        {
            new GeneratedArtifact(
                relativePath,
                GeneratedArtifactKind.Backend,
                appliedContent),
        };
        var snapshot = await GenerationWorkspaceStore.CaptureAsync(
            workspaceRoot,
            artifacts);
        var plan = GenerationWritePlanner.Plan(
            artifacts,
            snapshot.ExistingFiles,
            snapshot.PreviousManifest);
        Assert.IsTrue(plan.CanApply);
        await GenerationRollbackCheckpointStore.CreateAsync(
            workspaceRoot,
            ApplyRunId,
            plan);
        await GenerationWorkspaceStore.ApplyAsync(workspaceRoot, plan);

        var query = queryOverride ?? Substitute.For<IQueryExecutor>();
        if (queryOverride is null)
        {
            query.QuerySingleOrDefaultAsync<CodeGenerationRunRecord>(
                    CodeGenerationRunSql.FindById,
                    Arg.Any<object?>(),
                    Arg.Any<CancellationToken>())
                .Returns(CreateSucceededApplyRecord());
            query.QuerySingleOrDefaultAsync<CodeGenerationRunRecord>(
                    CodeGenerationRunSql.FindSucceededRollbackBySourceApplyRunId,
                    Arg.Any<object?>(),
                    Arg.Any<CancellationToken>())
                .Returns((CodeGenerationRunRecord?)null);
            query.QuerySingleOrDefaultAsync<CodeGenerationRunRecord>(
                    CodeGenerationRunSql.FindRunningRollbackBySourceApplyRunId,
                    Arg.Any<object?>(),
                    Arg.Any<CancellationToken>())
                .Returns((CodeGenerationRunRecord?)null);
        }

        var service = CreateService(workspaceRoot, command, query, retention: retention);
        return new RollbackFixture(service, relativePath);
    }

    private static async Task<ChainFixture> CreateChainFixtureAsync(
        string workspaceRoot,
        ICommandExecutor command)
    {
        const string relativePath = "Backend/Product.g.cs";
        const string firstContent = "first-product\n";
        const string secondContent = "second-product\n";
        var firstArtifacts = new[]
        {
            new GeneratedArtifact(
                relativePath,
                GeneratedArtifactKind.Backend,
                firstContent),
        };
        var firstSnapshot = await GenerationWorkspaceStore.CaptureAsync(
            workspaceRoot,
            firstArtifacts);
        var firstPlan = GenerationWritePlanner.Plan(
            firstArtifacts,
            firstSnapshot.ExistingFiles,
            firstSnapshot.PreviousManifest);
        Assert.IsTrue(firstPlan.CanApply);
        await GenerationRollbackCheckpointStore.CreateAsync(
            workspaceRoot,
            ApplyRunId,
            firstPlan);
        await GenerationWorkspaceStore.ApplyAsync(workspaceRoot, firstPlan);

        var secondArtifacts = new[]
        {
            new GeneratedArtifact(
                relativePath,
                GeneratedArtifactKind.Backend,
                secondContent),
        };
        var secondSnapshot = await GenerationWorkspaceStore.CaptureAsync(
            workspaceRoot,
            secondArtifacts);
        var secondPlan = GenerationWritePlanner.Plan(
            secondArtifacts,
            secondSnapshot.ExistingFiles,
            secondSnapshot.PreviousManifest);
        Assert.IsTrue(secondPlan.CanApply);
        await GenerationRollbackCheckpointStore.CreateAsync(
            workspaceRoot,
            ApplyRunId2,
            secondPlan);
        await GenerationWorkspaceStore.ApplyAsync(workspaceRoot, secondPlan);

        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<CodeGenerationRunRecord>(
                CodeGenerationRunSql.FindById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var id = Read<Guid>(call.ArgAt<object>(1)!, "Id");
                return id switch
                {
                    _ when id == ApplyRunId => CreateSucceededApplyRecord(
                        ApplyRunId,
                        Now.AddMinutes(-2)),
                    _ when id == ApplyRunId2 => CreateSucceededApplyRecord(
                        ApplyRunId2,
                        Now.AddMinutes(-1)),
                    _ => null,
                };
            });
        query.QueryAsync<Guid>(
                CodeGenerationRunSql.ListPendingRollbackApplies,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns([ApplyRunId2, ApplyRunId]);
        query.QuerySingleOrDefaultAsync<CodeGenerationRunRecord>(
                CodeGenerationRunSql.FindSucceededRollbackBySourceApplyRunId,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns((CodeGenerationRunRecord?)null);
        query.QuerySingleOrDefaultAsync<CodeGenerationRunRecord>(
                CodeGenerationRunSql.FindRunningRollbackBySourceApplyRunId,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns((CodeGenerationRunRecord?)null);

        return new ChainFixture(query);
    }

    private static CodeGenerationRollbackService CreateService(
        string workspaceRoot,
        ICommandExecutor command,
        IQueryExecutor query,
        CodeGenerationApplyGate? gate = null,
        CodeGenerationCheckpointRetentionOptions? retention = null,
        IIdGenerator? idGenerator = null) =>
        new(
            command,
            query,
            Options.Create(new CodeGenerationApplyOptions
            {
                Enabled = true,
                WorkspaceRoot = workspaceRoot,
            }),
            Options.Create(retention ?? new CodeGenerationCheckpointRetentionOptions()),
            gate ?? CodeGenerationApplyGateTestSupport.CreateLocalGate(@"C:\workspaces\codegen"),
            CodeGenerationGitWorkspaceTestSupport.CreateDisabled(workspaceRoot),
            new FixedClock(Now),
            idGenerator ?? new FixedIdGenerator(RollbackRunId),
            NullLogger<CodeGenerationRollbackService>.Instance);

    private static CodeGenerationRunRecord CreateSucceededApplyRecord(
        Guid id,
        DateTimeOffset finishedAtUtc) =>
        new()
        {
            Id = id,
            TemplateId = Guid.Parse("0198f36e-f7a7-7c52-9cbb-774e67411314"),
            TemplateVersion = 3,
            OperationKind = CodeGenerationRunOperationKinds.Apply,
            Status = CodeGenerationRunStatuses.Succeeded,
            ModuleKey = "catalog",
            EntityKey = "product",
            SchemaSha256 = new string('a', 64),
            ArtifactCount = 1,
            ManifestSha256 = new string('b', 64),
            RequestedByUserId = ActorUserId,
            StartedAtUtc = finishedAtUtc.AddMinutes(-1),
            FinishedAtUtc = finishedAtUtc,
        };

    private static CodeGenerationRunRecord CreateSucceededApplyRecord() =>
        CreateSucceededApplyRecord(ApplyRunId, Now.AddSeconds(-30));

    private static T Read<T>(object parameters, string name) =>
        (T)parameters.GetType().GetProperty(name)!.GetValue(parameters)!;

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }

    private sealed class FixedIdGenerator(Guid id) : IIdGenerator
    {
        public Guid NewId() => id;
    }

    private sealed class SequentialIdGenerator(params Guid[] ids) : IIdGenerator
    {
        private readonly Queue<Guid> _remaining = new(ids);

        public Guid NewId() => _remaining.Dequeue();
    }

    private sealed record RollbackFixture(
        CodeGenerationRollbackService Service,
        string RelativePath);

    private sealed record ChainFixture(IQueryExecutor Query);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = Directory.CreateTempSubdirectory(
                "fullnet-codegeneration-rollback-").FullName;
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
