using System.Security.Cryptography;
using System.Text;
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

[TestClass]
public sealed class CodeGenerationApplyServiceTests
{
    private static readonly Guid ActorUserId = Guid.Parse(
        "0198f36e-f7a7-7c52-9cbb-774e67411211");
    private static readonly Guid PreviewRunId = Guid.Parse(
        "0198f36e-f7a7-7c52-9cbb-774e67411212");
    private static readonly Guid ApplyRunId = Guid.Parse(
        "0198f36e-f7a7-7c52-9cbb-774e67411213");
    private static readonly Guid TemplateId = Guid.Parse(
        "0198f36e-f7a7-7c52-9cbb-774e67411214");
    private static readonly DateTimeOffset Now = new(
        2026,
        7,
        31,
        8,
        0,
        0,
        TimeSpan.Zero);

    [TestMethod]
    public async Task Reviewed_template_preview_applies_real_workspace_and_persists_summary()
    {
        using var workspace = new TemporaryDirectory();
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(
                CodeGenerationRunSql.Insert,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        command.ExecuteAsync(
                CodeGenerationRunSql.CompleteApply,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        var fixture = CreateService(workspace.Path, command);

        var result = await fixture.Service.ApplyAsync(
            ActorUserId,
            new CodeGenerationRunApplyRequest(PreviewRunId));

        Assert.IsTrue(result.IsSuccess, result.Error?.Code);
        Assert.AreEqual(ApplyRunId, result.Value!.RunId);
        Assert.AreEqual(PreviewRunId, result.Value.PreviewRunId);
        Assert.AreEqual(fixture.ArtifactCount, result.Value.ArtifactCount);
        Assert.IsTrue(result.Value.ChangedArtifactCount > 0);
        Assert.AreEqual(fixture.ManifestSha256, result.Value.ManifestSha256);
        Assert.IsTrue(File.Exists(Path.Combine(
            workspace.Path,
            GenerationWorkspaceStore.ManifestRelativePath)));
        var checkpoint = await GenerationRollbackCheckpointStore.ReadAsync(
            workspace.Path,
            ApplyRunId);
        Assert.AreEqual(ApplyRunId, checkpoint.ApplyRunId);
        Assert.IsNull(checkpoint.PreviousManifest);
        Assert.AreEqual(
            GenerationManifest.Parse(await File.ReadAllTextAsync(Path.Combine(
                workspace.Path,
                GenerationWorkspaceStore.ManifestRelativePath))).ToJson(),
            checkpoint.AppliedManifest.ToJson());
        await command.Received(1).ExecuteAsync(
            CodeGenerationRunSql.Insert,
            Arg.Is<object>(parameters =>
                parameters != null
                && Read<string>(parameters, "OperationKind")
                    == CodeGenerationRunOperationKinds.Apply
                && Read<string>(parameters, "Status")
                    == CodeGenerationRunStatuses.Running
                && Read<Guid?>(parameters, "TemplateId") == TemplateId
                && Read<long?>(parameters, "TemplateVersion") == 3
                && Read<string>(parameters, "ManifestSha256")
                    == fixture.ManifestSha256
                && Read<string?>(parameters, "ErrorCode") == null
                && Read<Guid>(parameters, "RequestedByUserId")
                    == ActorUserId),
            Arg.Any<CancellationToken>());
        await command.Received(1).ExecuteAsync(
            CodeGenerationRunSql.CompleteApply,
            Arg.Is<object>(parameters =>
                parameters != null
                && Read<Guid>(parameters, "Id") == ApplyRunId),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Failed_run_insert_restores_the_original_workspace()
    {
        using var workspace = new TemporaryDirectory();
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(
                CodeGenerationRunSql.Insert,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(0);
        var fixture = CreateService(workspace.Path, command);

        try
        {
            await fixture.Service.ApplyAsync(
                ActorUserId,
                new CodeGenerationRunApplyRequest(PreviewRunId));
        }
        catch (InvalidOperationException)
        {
            // 摘要失败允许向上传播，但磁盘补偿不变量必须独立成立。
        }

        Assert.IsFalse(File.Exists(Path.Combine(
            workspace.Path,
            GenerationWorkspaceStore.ManifestRelativePath)));
        Assert.IsEmpty(Directory.EnumerateFiles(
            workspace.Path,
            "*",
            SearchOption.AllDirectories));
    }

    [TestMethod]
    public async Task Missing_workspace_after_startup_marks_running_attempt_failed()
    {
        using var workspace = new TemporaryDirectory();
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(
                CodeGenerationRunSql.Insert,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        command.ExecuteAsync(
                CodeGenerationRunSql.FailApply,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        var fixture = CreateService(workspace.Path, command);
        Directory.Delete(workspace.Path, recursive: true);

        var result = await fixture.Service.ApplyAsync(
            ActorUserId,
            new CodeGenerationRunApplyRequest(PreviewRunId));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            CodeGenerationRunErrorCodes.ApplyFailed,
            result.Error!.Code);
        await command.Received(1).ExecuteAsync(
            CodeGenerationRunSql.FailApply,
            Arg.Is<object>(parameters =>
                parameters != null
                && Read<Guid>(parameters, "Id") == ApplyRunId
                && Read<string>(parameters, "ErrorCode")
                    == CodeGenerationRunErrorCodes.ApplyFailed),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Cancellation_after_running_insert_marks_attempt_failed()
    {
        using var workspace = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(
                CodeGenerationRunSql.Insert,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cancellation.Cancel();
                return 1;
            });
        command.ExecuteAsync(
                CodeGenerationRunSql.FailApply,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        var fixture = CreateService(workspace.Path, command);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => fixture.Service.ApplyAsync(
                ActorUserId,
                new CodeGenerationRunApplyRequest(PreviewRunId),
                cancellation.Token));

        await command.Received(1).ExecuteAsync(
            CodeGenerationRunSql.FailApply,
            Arg.Is<object>(parameters =>
                parameters != null
                && Read<Guid>(parameters, "Id") == ApplyRunId
                && Read<string>(parameters, "ErrorCode")
                    == CodeGenerationRunErrorCodes.ApplyFailed),
            Arg.Is<CancellationToken>(token => !token.CanBeCanceled));
        Assert.IsFalse(File.Exists(Path.Combine(
            workspace.Path,
            GenerationWorkspaceStore.ManifestRelativePath)));
    }

    [TestMethod]
    public async Task Deleted_template_marks_reviewed_preview_stale_without_writing()
    {
        using var workspace = new TemporaryDirectory();
        var command = Substitute.For<ICommandExecutor>();
        var fixture = CreateService(workspace.Path, command);
        fixture.Query.QuerySingleOrDefaultAsync<CodeGenerationTemplateRecord>(
                CodeGenerationTemplateSql.FindById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns((CodeGenerationTemplateRecord?)null);

        var result = await fixture.Service.ApplyAsync(
            ActorUserId,
            new CodeGenerationRunApplyRequest(PreviewRunId));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            CodeGenerationRunErrorCodes.StaleApplyPreview,
            result.Error!.Code);
        await command.DidNotReceive().ExecuteAsync(
            Arg.Any<SqlStatement>(),
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
        Assert.IsEmpty(Directory.EnumerateFiles(
            workspace.Path,
            "*",
            SearchOption.AllDirectories));
    }

    [TestMethod]
    public async Task Workspace_conflict_marks_running_attempt_failed_with_stable_code()
    {
        using var workspace = new TemporaryDirectory();
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(
                CodeGenerationRunSql.Insert,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        command.ExecuteAsync(
                CodeGenerationRunSql.FailApply,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        var fixture = CreateService(workspace.Path, command);
        var conflictPath = Path.Combine(
            workspace.Path,
            fixture.Artifacts[0].RelativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(conflictPath)!);
        await File.WriteAllTextAsync(conflictPath, "user-owned-content");

        var result = await fixture.Service.ApplyAsync(
            ActorUserId,
            new CodeGenerationRunApplyRequest(PreviewRunId));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            CodeGenerationRunErrorCodes.ApplyConflict,
            result.Error!.Code);
        Assert.AreEqual(
            "user-owned-content",
            await File.ReadAllTextAsync(conflictPath));
        await command.Received(1).ExecuteAsync(
            CodeGenerationRunSql.FailApply,
            Arg.Is<object>(parameters =>
                parameters != null
                && Read<string>(parameters, "ErrorCode")
                    == CodeGenerationRunErrorCodes.ApplyConflict),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Concurrent_apply_is_rejected_before_run_or_workspace_mutation()
    {
        using var workspace = new TemporaryDirectory();
        var command = Substitute.For<ICommandExecutor>();
        var fixture = CreateService(workspace.Path, command);
        Assert.IsTrue(await fixture.Gate.TryEnterAsync(CancellationToken.None));

        try
        {
            var result = await fixture.Service.ApplyAsync(
                ActorUserId,
                new CodeGenerationRunApplyRequest(PreviewRunId));

            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(
                CodeGenerationRunErrorCodes.ApplyBusy,
                result.Error!.Code);
            await command.DidNotReceive().ExecuteAsync(
                Arg.Any<SqlStatement>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>());
            Assert.IsEmpty(Directory.EnumerateFiles(
                workspace.Path,
                "*",
                SearchOption.AllDirectories));
        }
        finally
        {
            fixture.Gate.Release();
        }
    }

    [TestMethod]
    public async Task Existing_checkpoint_fails_running_attempt_before_workspace_mutation()
    {
        using var workspace = new TemporaryDirectory();
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(
                CodeGenerationRunSql.Insert,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        command.ExecuteAsync(
                CodeGenerationRunSql.FailApply,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        var fixture = CreateService(workspace.Path, command);
        Directory.CreateDirectory(Path.Combine(
            workspace.Path,
            GenerationRollbackCheckpointStore.RootRelativePath.Replace(
                '/',
                Path.DirectorySeparatorChar),
            ApplyRunId.ToString("N")));

        var result = await fixture.Service.ApplyAsync(
            ActorUserId,
            new CodeGenerationRunApplyRequest(PreviewRunId));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            CodeGenerationRunErrorCodes.ApplyFailed,
            result.Error!.Code);
        Assert.IsFalse(File.Exists(Path.Combine(
            workspace.Path,
            GenerationWorkspaceStore.ManifestRelativePath)));
        await command.Received(1).ExecuteAsync(
            CodeGenerationRunSql.FailApply,
            Arg.Is<object>(parameters =>
                parameters != null
                && Read<string>(parameters, "ErrorCode")
                    == CodeGenerationRunErrorCodes.ApplyFailed),
            Arg.Any<CancellationToken>());
    }

    private static ApplyFixture CreateService(
        string workspaceRoot,
        ICommandExecutor command)
    {
        var normalizer = new CodeGenerationSchemaNormalizer();
        var normalized = normalizer.Normalize(CreateSchema());
        Assert.IsTrue(
            normalized.IsSuccess,
            normalized.Error?.Code ?? "missing error code");
        var artifacts = CrudArtifactGenerator.Generate(normalized.Value!.Schema);
        var manifestSha256 = ComputeManifestSha256(artifacts);
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<CodeGenerationRunRecord>(
                CodeGenerationRunSql.FindById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(new CodeGenerationRunRecord
            {
                Id = PreviewRunId,
                TemplateId = TemplateId,
                TemplateVersion = 3,
                OperationKind = CodeGenerationRunOperationKinds.Preview,
                Status = CodeGenerationRunStatuses.Succeeded,
                ModuleKey = "catalog",
                EntityKey = "product",
                SchemaSha256 = normalized.Value.SchemaSha256,
                ArtifactCount = artifacts.Count,
                ManifestSha256 = manifestSha256,
                RequestedByUserId = ActorUserId,
                StartedAtUtc = Now.AddMinutes(-1),
                FinishedAtUtc = Now.AddSeconds(-30),
            });
        query.QuerySingleOrDefaultAsync<CodeGenerationTemplateRecord>(
                CodeGenerationTemplateSql.FindById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(new CodeGenerationTemplateRecord
            {
                Id = TemplateId,
                Name = "Product CRUD",
                SchemaJson = normalized.Value.CanonicalJson,
                SchemaSha256 = normalized.Value.SchemaSha256,
                CreatedAtUtc = Now.AddDays(-1),
                CreatedByUserId = ActorUserId,
                Version = 3,
            });
        var applyGate = CodeGenerationApplyGateTestSupport.CreateLocalGate(workspaceRoot);
        var service = new CodeGenerationApplyService(
            command,
            query,
            new CodeGenerationTemplateQueryService(
                query,
                Substitute.For<IMultiResultQueryExecutor>(),
                normalizer,
                Options.Create(new DatabaseOptions
                {
                    Provider = DatabaseProvider.SqlServer,
                })),
            normalizer,
            Options.Create(new CodeGenerationApplyOptions
            {
                Enabled = true,
                WorkspaceRoot = workspaceRoot,
            }),
            applyGate,
            new FixedClock(Now),
            new FixedIdGenerator(ApplyRunId));
        return new ApplyFixture(
            service,
            artifacts,
            manifestSha256,
            query,
            applyGate);
    }

    private static string ComputeManifestSha256(
        IReadOnlyList<GeneratedArtifact> artifacts)
    {
        var manifest = string.Concat(artifacts
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .Select(artifact =>
                $"{artifact.RelativePath}\n{ToMachineCode(artifact.Kind)}\n"
                + $"{GenerationContentHash.Compute(artifact.Content)}\n"));
        return Convert.ToHexString(SHA256.HashData(
                new UTF8Encoding(false, true).GetBytes(manifest)))
            .ToLowerInvariant();
    }

    private static string ToMachineCode(GeneratedArtifactKind kind) =>
        kind switch
        {
            GeneratedArtifactKind.Backend => "backend",
            GeneratedArtifactKind.VueClient => "vue_client",
            GeneratedArtifactKind.LayuiClient => "layui_client",
            GeneratedArtifactKind.Report => "report",
            GeneratedArtifactKind.MigrationTemplate => "migration_template",
            GeneratedArtifactKind.IntegrationTestTemplate =>
                "integration_test_template",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static CodeGenerationPreviewRequest CreateSchema() =>
        new(
            "acme",
            "catalog",
            "product",
            "acme_catalog_product",
            "Acme.Modules.Catalog",
            "Product",
            "products",
            "products",
            "HostOnly",
            true,
            [
                new("Id", "Id", "id", "Uuid", false, null, null, null),
                new("Name", "Name", "displayName", "String", false, 200, null, null),
                new("IsActive", "IsActive", "isActive", "Boolean", false, null, null, null),
                new("Version", "Version", "version", "Int64", false, null, null, null),
            ]);

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

    private sealed record ApplyFixture(
        CodeGenerationApplyService Service,
        IReadOnlyList<GeneratedArtifact> Artifacts,
        string ManifestSha256,
        IQueryExecutor Query,
        CodeGenerationApplyGate Gate)
    {
        public int ArtifactCount => Artifacts.Count;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = Directory.CreateTempSubdirectory(
                "fullnet-codegeneration-apply-").FullName;
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
