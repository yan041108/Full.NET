using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Modules.CodeGeneration.Configuration;
using Full.NET.Modules.CodeGeneration.Contracts;
using Full.NET.Modules.CodeGeneration.Features.ManageHostRuns;
using Full.NET.Modules.CodeGeneration.Features.ManageHostTemplates;
using Full.NET.Modules.CodeGeneration.Features.NormalizeCrudSchema;
using Full.NET.Modules.CodeGeneration.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.CodeGeneration;

/// <summary>审查完整计划后只提交冻结正文；幂等、漂移和数据库故障不能产生第二次 Apply。</summary>
[TestClass]
public sealed class CodeGenerationApplyServiceTests
{
    [TestMethod]
    public async Task Maintenance_reads_the_actual_phase_and_recovers_the_existing_operation_without_a_second_apply()
    {
        using var fixture = new Fixture();
        var plan = await fixture.Prepare();
        fixture.FailCompletion = true;
        Assert.IsFalse((await fixture.Service.ApplyAsync(fixture.Actor, new(plan.PlanId, plan.PlanSha256, Guid.CreateVersion7()))).IsSuccess);
        var state = await fixture.Workspace.GetAsync();
        Assert.IsTrue(state.IsSuccess, state.Error?.Code);
        Assert.AreEqual(plan.PlanId, state.Value!.PendingOperationId);
        Assert.AreEqual("locally_applied", state.Value.Phase);
        var wrong = await fixture.Workspace.RecoverAsync(new(Guid.CreateVersion7()));
        Assert.AreEqual(CodeGenerationRunErrorCodes.InvalidRecoveryOperation, wrong.Error!.Code);
        fixture.FailCompletion = false;
        var recovered = await fixture.Workspace.RecoverAsync(new(plan.PlanId));
        Assert.IsTrue(recovered.IsSuccess, recovered.Error?.Code);
        Assert.AreEqual("idle", recovered.Value!.Phase);
        Assert.IsNull(recovered.Value.PendingOperationId);
        Assert.AreEqual(1, fixture.Inserts);
        Assert.IsTrue((await fixture.Workspace.RecoverAsync(new(plan.PlanId))).IsSuccess);
        Assert.AreEqual(1, fixture.Completions);
    }

    [TestMethod]
    public async Task Maintenance_refuses_unknown_receipts_and_does_not_turn_CLI_work_into_a_Host_operation()
    {
        using var fixture = new Fixture();
        var unknown = await fixture.Workspace.RecoverAsync(new(Guid.CreateVersion7()));
        Assert.AreEqual(CodeGenerationRunErrorCodes.InvalidRecoveryOperation, unknown.Error!.Code);
        var id = Guid.CreateVersion7();
        await using (var lease = await fixture.Acquire())
        {
            GeneratedArtifact[] artifacts = [new("cli.txt", GeneratedArtifactKind.Backend, "cli")];
            var snapshot = await GenerationWorkspaceStore.CaptureAsync(fixture.Root, artifacts);
            await GenerationWorkspaceJournal.PrepareAsync(lease, id, id, "apply", "cli", null,
                GenerationWritePlanner.Plan(artifacts, snapshot.ExistingFiles, snapshot.PreviousManifest));
        }
        var rejected = await fixture.Workspace.RecoverAsync(new(id));
        Assert.AreEqual(CodeGenerationRunErrorCodes.InvalidRecoveryOperation, rejected.Error!.Code);
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Root, "cli.txt")));
        Assert.AreEqual(0, fixture.Inserts);
    }

    [TestMethod]
    public async Task Reserved_receipt_without_active_pointer_resumes_the_same_key_after_process_interruption()
    {
        using var fixture = new Fixture();
        var plan = await fixture.Prepare();
        var key = Guid.CreateVersion7();
        await using (var lease = await fixture.Acquire())
        {
            var json = await GenerationWorkspaceMetadata.ReadAsync(fixture.Root, $".fullnet/codegeneration-plans/{plan.PlanId:N}.json");
            var frozen = GenerationWorkspaceJournal.ParsePlan(lease, json!) with { IdempotencyKey = key };
            await GenerationWorkspaceJournal.ActivateAsync(lease, frozen);
            GenerationWorkspaceMetadata.Delete(fixture.Root, ".fullnet/codegeneration-active-operation.json");
        }
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Root, GenerationWorkspaceStore.ManifestRelativePath)));
        var resumed = await fixture.Service.ApplyAsync(fixture.Actor, new(plan.PlanId, plan.PlanSha256, key));
        Assert.IsTrue(resumed.IsSuccess, resumed.Error?.Code);
        Assert.AreEqual(plan.PlanId, resumed.Value!.RunId);
        Assert.AreEqual(1, fixture.Inserts);
    }

    [TestMethod]
    public async Task Preparing_full_plan_exposes_all_hashes_without_inserting_run_or_writing_product_files()
    {
        using var fixture = new Fixture();
        var plan = await fixture.Prepare();
        Assert.AreEqual(64, plan.PlanSha256.Length);
        Assert.AreEqual(0L, plan.WorkspaceRevision);
        Assert.IsTrue(plan.Files.Any(file => file.RelativePath == GenerationWorkspaceStore.ManifestRelativePath));
        Assert.IsTrue(plan.Files.All(file => file.BeforeSha256 is null && file.AfterSha256 is { Length: 64 }));
        Assert.AreEqual(0, fixture.Inserts);
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Root, GenerationWorkspaceStore.ManifestRelativePath)));
        await using var lease = await fixture.Acquire();
        Assert.IsNull(await GenerationWorkspaceJournal.ReadPendingAsync(lease));
    }

    [TestMethod]
    public async Task Confirmed_plan_commits_once_and_same_key_returns_original_result_after_response_loss()
    {
        using var fixture = new Fixture();
        var plan = await fixture.Prepare();
        var request = new CodeGenerationRunApplyRequest(plan.PlanId, plan.PlanSha256, Guid.CreateVersion7());
        var first = await fixture.Service.ApplyAsync(fixture.Actor, request);
        Assert.IsTrue(first.IsSuccess, first.Error?.Code);
        var replay = await fixture.Service.ApplyAsync(fixture.Actor, request);
        Assert.IsTrue(replay.IsSuccess, replay.Error?.Code);
        Assert.AreEqual(first.Value, replay.Value);
        Assert.AreEqual(1, fixture.Inserts);
        Assert.AreEqual(1, fixture.Completions);
        Assert.AreEqual("disabled", first.Value!.PublicationStatus);
        var checkpoint = await GenerationRollbackCheckpointStore.ReadAsync(fixture.Root, plan.PlanId);
        Assert.AreEqual(CodeGenerationRunSummary.ComputeManifestSha256(checkpoint.AppliedManifest), first.Value.ManifestSha256);
    }

    [TestMethod]
    public async Task Same_key_different_plan_and_different_key_same_plan_are_rejected()
    {
        using var fixture = new Fixture();
        var first = await fixture.Prepare();
        var second = await fixture.Prepare();
        var key = Guid.CreateVersion7();
        Assert.IsTrue((await fixture.Service.ApplyAsync(fixture.Actor, new(first.PlanId, first.PlanSha256, key))).IsSuccess);
        var otherPlan = await fixture.Service.ApplyAsync(fixture.Actor, new(second.PlanId, second.PlanSha256, key));
        var otherKey = await fixture.Service.ApplyAsync(fixture.Actor, new(first.PlanId, first.PlanSha256, Guid.CreateVersion7()));
        Assert.AreEqual(CodeGenerationRunErrorCodes.ApplyConflict, otherPlan.Error!.Code);
        Assert.AreEqual(CodeGenerationRunErrorCodes.ApplyConflict, otherKey.Error!.Code);
        Assert.AreEqual(1, fixture.Inserts);
    }

    [TestMethod]
    public async Task Later_workspace_revision_invalidates_an_older_plan_even_with_identical_generated_content()
    {
        using var fixture = new Fixture();
        var first = await fixture.Prepare();
        var stale = await fixture.Prepare();
        Assert.IsTrue((await fixture.Service.ApplyAsync(fixture.Actor, new(first.PlanId, first.PlanSha256, Guid.CreateVersion7()))).IsSuccess);
        var rejected = await fixture.Service.ApplyAsync(fixture.Actor, new(stale.PlanId, stale.PlanSha256, Guid.CreateVersion7()));
        Assert.AreEqual(CodeGenerationRunErrorCodes.ApplyConflict, rejected.Error!.Code);
        Assert.AreEqual(1, fixture.Inserts);
    }

    [TestMethod]
    public async Task Human_drift_after_review_is_preserved_before_run_insert()
    {
        using var fixture = new Fixture();
        var plan = await fixture.Prepare();
        var path = Path.Combine(fixture.Root, plan.Files.First(file => file.Kind == "create").RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "human-source");
        var result = await fixture.Service.ApplyAsync(fixture.Actor, new(plan.PlanId, plan.PlanSha256, Guid.CreateVersion7()));
        Assert.AreEqual(CodeGenerationRunErrorCodes.ApplyConflict, result.Error!.Code);
        Assert.AreEqual("human-source", await File.ReadAllTextAsync(path));
        Assert.AreEqual(0, fixture.Inserts);
    }

    [TestMethod]
    public async Task Reviewed_bytes_do_not_depend_on_a_mutable_or_deleted_template_at_execution_time()
    {
        using var fixture = new Fixture();
        var plan = await fixture.Prepare();
        fixture.Query.QuerySingleOrDefaultAsync<CodeGenerationTemplateRecord>(CodeGenerationTemplateSql.FindById,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns((CodeGenerationTemplateRecord?)null);
        var result = await fixture.Service.ApplyAsync(fixture.Actor, new(plan.PlanId, plan.PlanSha256, Guid.CreateVersion7()));
        Assert.IsTrue(result.IsSuccess, result.Error?.Code);
        Assert.AreEqual(1, fixture.Inserts);
    }

    [TestMethod]
    public async Task Expired_plan_or_different_reviewer_cannot_start_an_operation()
    {
        using var fixture = new Fixture();
        var plan = await fixture.Prepare();
        var other = await fixture.Service.ApplyAsync(Guid.CreateVersion7(), new(plan.PlanId, plan.PlanSha256, Guid.CreateVersion7()));
        Assert.AreEqual(CodeGenerationRunErrorCodes.StaleApplyPreview, other.Error!.Code);
        fixture.Clock.UtcNow = fixture.Clock.UtcNow.AddMinutes(16);
        var expired = await fixture.Service.ApplyAsync(fixture.Actor, new(plan.PlanId, plan.PlanSha256, Guid.CreateVersion7()));
        Assert.AreEqual(CodeGenerationRunErrorCodes.StaleApplyPreview, expired.Error!.Code);
        Assert.AreEqual(0, fixture.Inserts);
    }

    [TestMethod]
    public async Task Database_terminal_failure_keeps_running_evidence_and_same_key_recovers_without_second_insert()
    {
        using var fixture = new Fixture();
        var plan = await fixture.Prepare();
        var request = new CodeGenerationRunApplyRequest(plan.PlanId, plan.PlanSha256, Guid.CreateVersion7());
        fixture.FailCompletion = true;
        var interrupted = await fixture.Service.ApplyAsync(fixture.Actor, request);
        Assert.IsFalse(interrupted.IsSuccess);
        Assert.AreEqual(CodeGenerationRunErrorCodes.RecoveryRequired, interrupted.Error!.Code);
        Assert.IsTrue(File.Exists(Path.Combine(fixture.Root, GenerationWorkspaceStore.ManifestRelativePath)));
        Assert.AreEqual("running", fixture.Runs[plan.PlanId].Status);
        await using (var lease = await fixture.Acquire()) Assert.IsNotNull(await GenerationWorkspaceJournal.ReadPendingAsync(lease));
        fixture.FailCompletion = false;
        var recovered = await fixture.Service.ApplyAsync(fixture.Actor, request);
        Assert.IsTrue(recovered.IsSuccess, recovered.Error?.Code);
        Assert.AreEqual(plan.PlanId, recovered.Value!.RunId);
        Assert.AreEqual(1, fixture.Inserts);
        await using var after = await fixture.Acquire();
        Assert.IsNull(await GenerationWorkspaceJournal.ReadPendingAsync(after));
    }

    [TestMethod]
    public async Task Missing_database_row_after_insert_failure_is_recreated_from_the_same_durable_operation()
    {
        using var fixture = new Fixture();
        var plan = await fixture.Prepare();
        var request = new CodeGenerationRunApplyRequest(plan.PlanId, plan.PlanSha256, Guid.CreateVersion7());
        fixture.FailInsert = true;
        Assert.IsFalse((await fixture.Service.ApplyAsync(fixture.Actor, request)).IsSuccess);
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Root, GenerationWorkspaceStore.ManifestRelativePath)));
        fixture.FailInsert = false;
        Assert.IsTrue((await fixture.Service.ApplyAsync(fixture.Actor, request)).IsSuccess);
        Assert.AreEqual(1, fixture.Inserts);
        Assert.AreEqual(1, fixture.Runs.Count(run => run.Value.OperationKind == "apply"));
    }

    [TestMethod]
    public async Task Disabled_or_busy_workspace_rejects_before_product_changes()
    {
        using var fixture = new Fixture();
        fixture.Options.Enabled = false;
        var disabled = await fixture.Service.PrepareAsync(fixture.Actor, new(fixture.PreviewId));
        Assert.AreEqual(CodeGenerationRunErrorCodes.ApplyDisabled, disabled.Error!.Code);
        fixture.Options.Enabled = true;
        Assert.IsTrue(await fixture.Gate.TryEnterAsync(CancellationToken.None));
        try
        {
            var busy = await fixture.Service.PrepareAsync(fixture.Actor, new(fixture.PreviewId));
            Assert.AreEqual(CodeGenerationRunErrorCodes.ApplyBusy, busy.Error!.Code);
            Assert.AreEqual(0, fixture.Inserts);
        }
        finally { await fixture.Gate.ReleaseAsync(); }
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory("fullnet-codegeneration-reviewed-").FullName;
        internal Guid Actor { get; } = Guid.CreateVersion7();
        internal Guid PreviewId { get; } = Guid.CreateVersion7();
        internal Dictionary<Guid, CodeGenerationRunRecord> Runs { get; } = [];
        internal IQueryExecutor Query { get; } = Substitute.For<IQueryExecutor>();
        internal CodeGenerationApplyOptions Options { get; }
        internal CodeGenerationApplyGate Gate { get; }
        internal CodeGenerationApplyService Service { get; }
        internal CodeGenerationWorkspaceService Workspace { get; }
        internal MutableClock Clock { get; } = new();
        internal CodeGenerationArtifactTestStore Archives { get; }
        private string _schemaJson = string.Empty;
        private IReadOnlyList<GeneratedArtifact> _artifacts = [];
        internal bool FailInsert { get; set; }
        internal bool FailCompletion { get; set; }
        internal int Inserts { get; private set; }
        internal int Completions { get; private set; }

        internal Fixture()
        {
            var normalizer = new CodeGenerationSchemaNormalizer();
            var normalized = normalizer.Normalize(new("acme", "catalog", "product", "acme_catalog_product", "Acme.Modules.Catalog", "Product",
                "products", "products", "HostOnly", true, [new("Id", "Id", "id", "Uuid", false, null, null, null),
                    new("Name", "Name", "name", "String", false, 200, null, null), new("IsActive", "IsActive", "isActive", "Boolean", false, null, null, null),
                    new("Version", "Version", "version", "Int64", false, null, null, null)]));
            Assert.IsTrue(normalized.IsSuccess);
            Archives = new(Clock);
            _schemaJson = normalized.Value!.CanonicalJson;
            var templateId = Guid.CreateVersion7();
            var artifacts = CrudArtifactGenerator.Generate(normalized.Value!.Schema);
            _artifacts = artifacts;
            Runs[PreviewId] = new() { Id = PreviewId, TemplateId = templateId, TemplateVersion = 3, OperationKind = "preview", Status = "succeeded",
                ModuleKey = "catalog", EntityKey = "product", SchemaSha256 = normalized.Value.SchemaSha256, ArtifactCount = artifacts.Count,
                ManifestSha256 = CodeGenerationRunSummary.ComputeManifestSha256(artifacts), RequestedByUserId = Actor };
            Query.QuerySingleOrDefaultAsync<CodeGenerationRunRecord>(CodeGenerationRunSql.FindById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(call => Runs.GetValueOrDefault(ReadSqlParameter<Guid>(call.ArgAt<object>(1), "Id")));
            Query.QuerySingleOrDefaultAsync<CodeGenerationTemplateRecord>(CodeGenerationTemplateSql.FindById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(new CodeGenerationTemplateRecord { Id = templateId, Name = "Product", Version = 3, SchemaJson = normalized.Value.CanonicalJson,
                    SchemaSha256 = normalized.Value.SchemaSha256, CreatedAtUtc = Clock.UtcNow, CreatedByUserId = Actor });
            var command = Substitute.For<ICommandExecutor>();
            command.ExecuteAsync(CodeGenerationRunSql.Insert, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                if (FailInsert) throw new IOException("模拟数据库插入暂时不可用。");
                var parameter = call.ArgAt<object>(1);
                var run = new CodeGenerationRunRecord { Id = ReadSqlParameter<Guid>(parameter, "Id"), TemplateId = ReadSqlParameter<Guid?>(parameter, "TemplateId"),
                    TemplateVersion = ReadSqlParameter<long?>(parameter, "TemplateVersion"), SourceApplyRunId = ReadSqlParameter<Guid?>(parameter, "SourceApplyRunId"),
                    OperationKind = ReadSqlParameter<string>(parameter, "OperationKind"), Status = ReadSqlParameter<string>(parameter, "Status"),
                    ModuleKey = ReadSqlParameter<string>(parameter, "ModuleKey"), EntityKey = ReadSqlParameter<string>(parameter, "EntityKey"),
                    SchemaSha256 = ReadSqlParameter<string>(parameter, "SchemaSha256"), ArtifactCount = ReadSqlParameter<int>(parameter, "ArtifactCount"),
                    ManifestSha256 = ReadSqlParameter<string>(parameter, "ManifestSha256"), RequestedByUserId = ReadSqlParameter<Guid>(parameter, "RequestedByUserId"),
                    StartedAtUtc = ReadSqlParameter<DateTimeOffset>(parameter, "StartedAtUtc"), FinishedAtUtc = ReadSqlParameter<DateTimeOffset>(parameter, "FinishedAtUtc") };
                Runs.Add(run.Id, run);
                Inserts++;
                return 1;
            });
            command.ExecuteAsync(CodeGenerationRunSql.CompleteApply, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ =>
            {
                if (FailCompletion) throw new IOException("模拟数据库终态暂时不可用。");
                Completions++;
                return 1;
            });
            Options = new() { Enabled = true, WorkspaceRoot = Root };
            Gate = CodeGenerationApplyGateTestSupport.CreateLocalGate(Root);
            var ids = Substitute.For<IIdGenerator>();
            ids.NewId().Returns(_ => Guid.CreateVersion7());
            Service = new(command, Query, normalizer,
                Microsoft.Extensions.Options.Options.Create(Options), Gate, CodeGenerationGitWorkspaceTestSupport.CreateDisabled(Root), Clock, ids, Archives.Store);
            Workspace = new(command, Query, Microsoft.Extensions.Options.Options.Create(Options), Gate,
                CodeGenerationGitWorkspaceTestSupport.CreateDisabled(Root), Clock, Archives.Store);
        }

        internal async Task<CodeGenerationRunApplyPlanResponse> Prepare()
        {
            await Archives.Store.SaveAsync(PreviewId, _schemaJson, CrudArtifactGenerator.GeneratorVersion, Runs[PreviewId].ManifestSha256!,
                Clock.UtcNow, "preview.zip", _artifacts, default);
            var result = await Service.PrepareAsync(Actor, new(PreviewId));
            Assert.IsTrue(result.IsSuccess, result.Error?.Code);
            return result.Value!;
        }
        internal async Task<GenerationWorkspaceLease> Acquire() => await GenerationWorkspaceLease.TryAcquireAsync(Root) ?? throw new InvalidOperationException();
        public void Dispose() { Archives.Dispose(); Directory.Delete(Root, recursive: true); }
    }

    [TestMethod]
    public async Task Inline_archived_preview_can_be_planned_after_template_deletion_and_expiry_is_explicit()
    {
        using var fixture = new Fixture();
        await fixture.Prepare();
        var preview = fixture.Runs[fixture.PreviewId];
        fixture.Runs[fixture.PreviewId] = new() { Id = preview.Id, OperationKind = "preview", Status = "succeeded", TemplateId = null, TemplateVersion = null,
            ModuleKey = preview.ModuleKey, EntityKey = preview.EntityKey, SchemaSha256 = preview.SchemaSha256,
            ArtifactCount = preview.ArtifactCount, ManifestSha256 = preview.ManifestSha256, RequestedByUserId = preview.RequestedByUserId };
        fixture.Query.QuerySingleOrDefaultAsync<CodeGenerationTemplateRecord>(CodeGenerationTemplateSql.FindById,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns((CodeGenerationTemplateRecord?)null);
        var plan = await fixture.Service.PrepareAsync(fixture.Actor, new(fixture.PreviewId));
        Assert.IsTrue(plan.IsSuccess, plan.Error?.Code);
        fixture.Clock.UtcNow = fixture.Clock.UtcNow.AddDays(31);
        var expired = await fixture.Service.PrepareAsync(fixture.Actor, new(fixture.PreviewId));
        Assert.AreEqual(CodeGenerationRunErrorCodes.ArchiveExpired, expired.Error!.Code);
        Assert.AreEqual(0, fixture.Inserts);
    }

    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);
    }
}
