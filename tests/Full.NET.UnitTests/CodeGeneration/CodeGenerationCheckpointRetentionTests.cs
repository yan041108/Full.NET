using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Modules.CodeGeneration;
using Full.NET.Modules.CodeGeneration.Configuration;
using Full.NET.Modules.CodeGeneration.Persistence;
using Full.NET.Modules.CodeGeneration.Retention;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class CodeGenerationCheckpointRetentionTests
{
    private static readonly Guid ApplyRunId = Guid.Parse(
        "0198f7b3-34af-704c-8d2e-fc6aec9bf201");

    [TestMethod]
    public void Background_registration_uses_disabled_defaults_and_rejects_unsafe_bounds()
    {
        using var defaults = CreateProvider(new Dictionary<string, string?>());
        var options = defaults.GetRequiredService<
            IOptions<CodeGenerationCheckpointRetentionOptions>>().Value;

        Assert.IsFalse(options.Enabled);
        Assert.AreEqual(7, options.RetentionDays);
        Assert.AreEqual(3600, options.PollSeconds);
        Assert.AreEqual(20, options.MaxDeletesPerRun);

        using var invalid = CreateProvider(
            new Dictionary<string, string?>
            {
                ["CodeGeneration:CheckpointRetention:RetentionDays"] = "0",
                ["CodeGeneration:CheckpointRetention:PollSeconds"] = "59",
                ["CodeGeneration:CheckpointRetention:MaxDeletesPerRun"] = "0",
            });
        var exception = Assert.ThrowsExactly<OptionsValidationException>(
            invalid.GetRequiredService<IStartupValidator>().Validate);

        Assert.AreEqual(3, exception.Failures.Count());
    }

    [TestMethod]
    public async Task Disabled_retention_does_not_query_database()
    {
        var query = new RecordingQueryExecutor([]);
        var runner = CreateRunner(
            query,
            enabledApply: false,
            workspaceRoot: @"C:\fullnet\codegen-workspace");

        var result = await runner.RunOnceAsync(
            new CodeGenerationCheckpointRetentionOptions { Enabled = false },
            CancellationToken.None);

        Assert.AreEqual(0, result.Scanned);
        Assert.AreEqual(0, query.Statements.Count);
    }

    [TestMethod]
    public async Task Runner_deletes_eligible_checkpoint_when_workspace_matches_previous_manifest()
    {
        using var workspace = new RetentionWorkspace();
        var plan = await workspace.CreateCheckpointPlanAsync();
        await GenerationRollbackCheckpointStore.CreateAsync(
            workspace.Path,
            ApplyRunId,
            plan);

        var query = new RecordingQueryExecutor(
        [
            new CodeGenerationCheckpointCleanupCandidate
            {
                ApplyRunId = ApplyRunId,
            },
        ]);
        var runner = CreateRunner(query, enabledApply: true, workspaceRoot: workspace.Path);
        var result = await runner.RunOnceAsync(
            new CodeGenerationCheckpointRetentionOptions
            {
                Enabled = true,
                RetentionDays = 1,
                MaxDeletesPerRun = 5,
            },
            CancellationToken.None);

        Assert.AreEqual(1, result.Scanned);
        Assert.AreEqual(1, result.Deleted);
        Assert.AreEqual(0, result.Skipped);
        Assert.AreEqual(0, result.Failed);
        Assert.IsFalse(Directory.Exists(workspace.GetCheckpointPath()));
    }

    [TestMethod]
    public async Task Runner_skips_when_workspace_manifest_drifted_after_rollback()
    {
        using var workspace = new RetentionWorkspace();
        var plan = await workspace.CreateCheckpointPlanAsync();
        await GenerationRollbackCheckpointStore.CreateAsync(
            workspace.Path,
            ApplyRunId,
            plan);
        workspace.Write(
            GenerationWorkspaceStore.ManifestRelativePath,
            GenerationManifest.Create(
            [
                new GenerationManifestEntry(
                    "backend/Product.g.cs",
                    GenerationContentHash.Compute("drifted-content\n")),
            ]).ToJson());
        workspace.Write("backend/Product.g.cs", "drifted-content\n");

        var query = new RecordingQueryExecutor(
        [
            new CodeGenerationCheckpointCleanupCandidate
            {
                ApplyRunId = ApplyRunId,
            },
        ]);
        var runner = CreateRunner(query, enabledApply: true, workspaceRoot: workspace.Path);
        var result = await runner.RunOnceAsync(
            new CodeGenerationCheckpointRetentionOptions
            {
                Enabled = true,
                RetentionDays = 1,
            },
            CancellationToken.None);

        Assert.AreEqual(1, result.Scanned);
        Assert.AreEqual(0, result.Deleted);
        Assert.AreEqual(1, result.Skipped);
        Assert.IsTrue(Directory.Exists(workspace.GetCheckpointPath()));
    }

    private static CodeGenerationCheckpointRetentionRunner CreateRunner(
        IQueryExecutor query,
        bool enabledApply,
        string workspaceRoot) =>
        new(
            query,
            Options.Create(new CodeGenerationApplyOptions
            {
                Enabled = enabledApply,
                WorkspaceRoot = workspaceRoot,
            }),
            Options.Create(new DatabaseOptions
            {
                Provider = DatabaseProvider.SqlServer,
            }),
            new FixedClock());

    private static ServiceProvider CreateProvider(
        IReadOnlyDictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        new CodeGenerationModule().AddBackgroundServices(services, configuration);
        return services.BuildServiceProvider();
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } =
            new(2026, 8, 2, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class RecordingQueryExecutor(
        IReadOnlyList<CodeGenerationCheckpointCleanupCandidate> candidates)
        : IQueryExecutor
    {
        public List<SqlStatement> Statements { get; } = [];

        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                $"Unexpected single-row statement '{statement.Name}'.");

        public Task<IReadOnlyList<T>> QueryAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            Statements.Add(statement);
            return Task.FromResult<IReadOnlyList<T>>(
                candidates.Cast<T>().ToArray());
        }
    }

    private sealed class RetentionWorkspace : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "fullnet-codegen-retention-" + Guid.NewGuid().ToString("N"));

        public RetentionWorkspace()
        {
            Directory.CreateDirectory(Path);
        }

        public async Task<GenerationWritePlan> CreateCheckpointPlanAsync()
        {
            const string relativePath = "backend/Product.g.cs";
            const string previousContent = "old-content\n";
            const string nextContent = "new-content\n";
            Write(relativePath, previousContent);
            Write(
                GenerationWorkspaceStore.ManifestRelativePath,
                GenerationManifest.Create(
                [
                    new GenerationManifestEntry(
                        relativePath,
                        GenerationContentHash.Compute(previousContent)),
                ]).ToJson());
            var artifacts = new[]
            {
                new GeneratedArtifact(
                    relativePath,
                    GeneratedArtifactKind.Backend,
                    nextContent),
            };
            var snapshot = await GenerationWorkspaceStore.CaptureAsync(
                Path,
                artifacts);
            return GenerationWritePlanner.Plan(
                artifacts,
                snapshot.ExistingFiles,
                snapshot.PreviousManifest);
        }

        public void Write(string relativePath, string content)
        {
            var fullPath = System.IO.Path.Combine(
                Path,
                relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
        }

        public string GetCheckpointPath() =>
            System.IO.Path.Combine(
                Path,
                GenerationRollbackCheckpointStore.RootRelativePath.Replace(
                    '/',
                    System.IO.Path.DirectorySeparatorChar),
                ApplyRunId.ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}