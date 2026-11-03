using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Modules.CodeGeneration.Configuration;
using Full.NET.Modules.CodeGeneration.Git;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.CodeGeneration.Contracts;
using Full.NET.Modules.CodeGeneration.Features.ManageHostRuns;
using Full.NET.Modules.CodeGeneration.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.CodeGeneration;

/// <summary>仅使用本机临时 bare 仓库验证提交与发布恢复，禁止触及项目远端。</summary>
[TestClass]
public sealed class CodeGenerationGitPublicationTests
{
    [TestMethod]
    public async Task Unknown_publication_version_or_field_is_rejected_without_invoking_git_or_overwriting_evidence()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.IsNull(await fixture.Service.SynchronizeAsync(default));
        var plan = await fixture.PlanAsync();
        var json = fixture.Service.CapturePublicationContext(fixture.RunId, "strict recovery", plan);
        var version = json.Replace("\"schemaVersion\":1", "\"schemaVersion\":2", StringComparison.Ordinal);
        Assert.AreNotEqual(json, version);
        Assert.ThrowsExactly<System.Text.Json.JsonException>(() => CodeGenerationGitWorkspaceService.ParsePublication(version));
        Assert.ThrowsExactly<System.Text.Json.JsonException>(() => CodeGenerationGitWorkspaceService.ParsePublication(json[..^1] + ",\"extra\":true}"));
        await GenerationWorkspaceMetadata.WriteAsync(fixture.Work, ".fullnet/codegeneration-git-publication.json", version);
        Assert.AreEqual("pending", (await fixture.Service.RetryPublicationAsync(fixture.RunId, default)).Status);
        Assert.AreEqual(version, await GenerationWorkspaceMetadata.ReadAsync(fixture.Work, ".fullnet/codegeneration-git-publication.json"));
        Assert.AreEqual("1", (await fixture.GitAsync(["rev-list", "--count", "HEAD"])).Trim());
    }

    [TestMethod]
    public async Task Fresh_service_recovers_the_frozen_Git_baseline_without_resynchronizing_dirty_source_or_duplicating_commit()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.IsNull(await fixture.Service.SynchronizeAsync(CancellationToken.None));
        var plan = await fixture.PlanAsync();
        var context = fixture.Service.CapturePublicationContext(fixture.RunId, "frozen recovery", plan);
        Assert.IsTrue(fixture.Service.MatchesPublicationBaseline(context));
        await GenerationWorkspaceStore.ApplyAsync(fixture.Work, plan);
        var fresh = fixture.NewService();
        Assert.IsFalse(fresh.MatchesPublicationBaseline(context));
        var published = await fresh.PublishFrozenAsync(context, CancellationToken.None);
        Assert.AreEqual("published", published.Status);
        var replay = await fresh.PublishFrozenAsync(context, CancellationToken.None);
        Assert.AreEqual(published.CommitSha, replay.CommitSha);
        Assert.AreEqual("published", replay.Status);
        Assert.AreEqual("2", (await fixture.GitAsync(["rev-list", "--count", "HEAD"])).Trim());
        Assert.AreEqual(published.CommitSha, await fixture.RemoteHeadAsync());
    }

    [TestMethod]
    public async Task Rejected_push_preserves_commit_and_retry_publishes_the_same_sha()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.IsNull(await fixture.Service.SynchronizeAsync(CancellationToken.None));
        var plan = await fixture.ApplyAsync();
        fixture.Runner.RejectPush = true;
        var result = await fixture.Service.PublishAsync(fixture.RunId, "generated change", plan, CancellationToken.None);
        Assert.AreEqual("pending", result.Status);
        var sha = await fixture.HeadAsync();
        Assert.AreEqual(sha, result.CommitSha);
        Assert.IsNotNull(await fixture.Service.SynchronizeAsync(CancellationToken.None));
        Assert.AreEqual(sha, await fixture.HeadAsync());
        Assert.AreEqual("generated\n", File.ReadAllText(Path.Combine(fixture.Work, "generated.cs")));
        fixture.Runner.RejectPush = false;
        var retried = await fixture.Service.RetryPublicationAsync(fixture.RunId, CancellationToken.None);
        Assert.AreEqual("published", retried.Status);
        Assert.AreEqual(sha, retried.CommitSha);
        Assert.AreEqual(sha, await fixture.RemoteHeadAsync());
        Assert.IsNull(await fixture.Service.SynchronizeAsync(CancellationToken.None));
        Assert.IsFalse(fixture.Runner.Commands.Any(arguments => arguments.Contains("reset") || arguments.Contains("--force") || arguments.Contains("-A")));
    }

    [TestMethod]
    [DataRow("push")]
    [DataRow("commit")]
    public async Task Lost_success_response_does_not_duplicate_commits_or_regenerate_files(string operation)
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.IsNull(await fixture.Service.SynchronizeAsync(CancellationToken.None));
        var plan = await fixture.ApplyAsync();
        fixture.Runner.LoseReplyFor = operation;
        var result = await fixture.Service.PublishAsync(fixture.RunId, "generated change", plan, CancellationToken.None);
        Assert.AreEqual("published", result.Status);
        var repeated = await fixture.Service.RetryPublicationAsync(fixture.RunId, CancellationToken.None);
        Assert.AreEqual(result.CommitSha, repeated.CommitSha);
        Assert.AreEqual("2", (await fixture.GitAsync(["rev-list", "--count", "HEAD"])).Trim());
        Assert.AreEqual(result.CommitSha, await fixture.RemoteHeadAsync());
    }

    [TestMethod]
    public async Task Unrelated_staged_file_blocks_commit_and_is_preserved()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.IsNull(await fixture.Service.SynchronizeAsync(CancellationToken.None));
        var baseline = await fixture.HeadAsync();
        var plan = await fixture.ApplyAsync();
        File.WriteAllText(Path.Combine(fixture.Work, "manual.txt"), "manual");
        await fixture.GitAsync(["add", "--", "manual.txt"]);
        var result = await fixture.Service.PublishAsync(fixture.RunId, "generated change", plan, CancellationToken.None);
        Assert.AreEqual("pending", result.Status);
        Assert.AreEqual(baseline, await fixture.HeadAsync());
        Assert.AreEqual("manual.txt", (await fixture.GitAsync(["diff", "--cached", "--name-only"])).Trim());
        Assert.AreEqual("manual", File.ReadAllText(Path.Combine(fixture.Work, "manual.txt")));
    }

    [TestMethod]
    public async Task Source_drift_after_local_apply_is_not_committed()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.IsNull(await fixture.Service.SynchronizeAsync(CancellationToken.None));
        var baseline = await fixture.HeadAsync();
        var plan = await fixture.ApplyAsync();
        File.WriteAllText(Path.Combine(fixture.Work, "generated.cs"), "manual edit");
        var result = await fixture.Service.PublishAsync(fixture.RunId, "generated change", plan, CancellationToken.None);
        Assert.AreEqual("pending", result.Status);
        Assert.AreEqual(baseline, await fixture.HeadAsync());
        Assert.AreEqual("manual edit", File.ReadAllText(Path.Combine(fixture.Work, "generated.cs")));
    }

    [TestMethod]
    public async Task Repository_line_ending_rules_do_not_break_the_frozen_worktree_or_commit_checks()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.IsNull(await fixture.Service.SynchronizeAsync(CancellationToken.None));
        var plan = await fixture.ApplyAsync("generated\r\n");
        var result = await fixture.Service.PublishAsync(fixture.RunId, "line endings", plan, CancellationToken.None);
        Assert.AreEqual("published", result.Status);
        Assert.AreEqual("generated\r\n", File.ReadAllText(Path.Combine(fixture.Work, "generated.cs")));
    }

    [TestMethod]
    public async Task Local_only_mode_keeps_a_persistent_commit_chain()
    {
        await using var fixture = await Fixture.CreateAsync(push: false);
        Assert.IsNull(await fixture.Service.SynchronizeAsync(CancellationToken.None));
        var first = await fixture.Service.PublishAsync(fixture.RunId, "first", await fixture.ApplyAsync(), CancellationToken.None);
        Assert.AreEqual("local_only", first.Status);
        Assert.IsNull(await fixture.Service.SynchronizeAsync(CancellationToken.None));
        var second = await fixture.Service.PublishAsync(Guid.NewGuid(), "second", await fixture.ApplyAsync("next\n"), CancellationToken.None);
        Assert.AreEqual("local_only", second.Status);
        Assert.AreNotEqual(first.CommitSha, second.CommitSha);
        Assert.AreEqual(first.CommitSha, (await fixture.GitAsync(["rev-parse", "HEAD^"])).Trim());
        Assert.AreEqual("next\n", File.ReadAllText(Path.Combine(fixture.Work, "generated.cs")));
    }

    [TestMethod]
    public async Task Publication_retry_service_reuses_the_saved_commit_and_updates_history_receipt()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.IsNull(await fixture.Service.SynchronizeAsync(CancellationToken.None));
        fixture.Runner.RejectPush = true;
        var applied = await fixture.Service.PublishAsync(fixture.RunId, "retry service", await fixture.ApplyAsync(), CancellationToken.None);
        Assert.AreEqual("pending", applied.Status);
        await fixture.ReleaseLeaseAsync();
        fixture.Runner.RejectPush = false;
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<CodeGenerationRunRecord>(CodeGenerationRunSql.FindById,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new CodeGenerationRunRecord
            { Id = fixture.RunId, OperationKind = CodeGenerationRunOperationKinds.Apply, Status = CodeGenerationRunStatuses.Succeeded });
        var retry = new CodeGenerationPublicationRetryService(query,
            Options.Create(new CodeGenerationApplyOptions { Enabled = true, WorkspaceRoot = fixture.Work }),
            CodeGenerationApplyGateTestSupport.CreateLocalGate(fixture.Work), fixture.Service);
        var result = await retry.RetryAsync(fixture.RunId, CancellationToken.None);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("published", result.Value!.PublicationStatus);
        Assert.AreEqual(applied.CommitSha, result.Value.CommitSha);
        var saved = await fixture.Service.GetSavedPublicationAsync(fixture.RunId, CancellationToken.None);
        Assert.AreEqual("published", saved.Status);
        Assert.AreEqual(applied.CommitSha, saved.CommitSha);
        Assert.AreEqual("2", (await fixture.GitAsync(["rev-list", "--count", "HEAD"])).Trim());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("fullnet-codegen-git-publication-").FullName;
        private GenerationWorkspaceLease? _lease;
        private readonly ProcessCodeGenerationGitCommandRunner _git = new();
        internal Guid RunId { get; } = Guid.NewGuid();
        internal string Work => Path.Combine(_root, "work");
        internal FaultRunner Runner { get; }
        internal CodeGenerationGitWorkspaceService Service { get; }

        private Fixture(bool push)
        {
            Runner = new FaultRunner(_git);
            Service = NewService(push);
        }

        internal CodeGenerationGitWorkspaceService NewService(bool push = true) => new(Options.Create(new CodeGenerationGitOptions
            {
                Enabled = true, PushEnabled = push, WorkingBranch = "codex/generated", AuthorName = "Full.NET Test",
                AuthorEmail = "codegen-test@localhost",
            }), Options.Create(new CodeGenerationApplyOptions { Enabled = true, WorkspaceRoot = Work }), Runner,
                NullLogger<CodeGenerationGitWorkspaceService>.Instance);

        internal static async Task<Fixture> CreateAsync(bool push = true)
        {
            var fixture = new Fixture(push);
            try
            {
                Directory.CreateDirectory(fixture.Work);
                await fixture.RunGitAsync(fixture._root, ["init", "--bare", "origin.git"]);
                await fixture.GitAsync(["init", "--initial-branch=main"]);
                File.WriteAllText(Path.Combine(fixture.Work, ".gitignore"), ".fullnet/\n");
                File.WriteAllText(Path.Combine(fixture.Work, ".gitattributes"), "* text=auto eol=lf\n");
                File.WriteAllText(Path.Combine(fixture.Work, "baseline.txt"), "baseline\n");
                await fixture.GitAsync(["add", "--", ".gitignore", ".gitattributes", "baseline.txt"]);
                await fixture.GitAsync(["-c", "user.name=Full.NET Test", "-c", "user.email=codegen-test@localhost", "commit", "-m", "baseline"]);
                await fixture.GitAsync(["remote", "add", "origin", Path.Combine(fixture._root, "origin.git")]);
                await fixture.GitAsync(["push", "origin", "main"]);
                await fixture.GitAsync(["checkout", "-b", "codex/generated"]);
                fixture._lease = await GenerationWorkspaceLease.TryAcquireAsync(fixture.Work);
                Assert.IsNotNull(fixture._lease);
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        internal async Task<GenerationWritePlan> ApplyAsync(string content = "generated\n")
        {
            var plan = await PlanAsync(content);
            await GenerationWorkspaceStore.ApplyAsync(Work, plan);
            return plan;
        }

        internal async Task<GenerationWritePlan> PlanAsync(string content = "generated\n")
        {
            GeneratedArtifact[] artifacts = [new("generated.cs", GeneratedArtifactKind.Backend, content)];
            var snapshot = await GenerationWorkspaceStore.CaptureAsync(Work, artifacts);
            var plan = GenerationWritePlanner.Plan(artifacts, snapshot.ExistingFiles, snapshot.PreviousManifest);
            return plan;
        }

        internal Task<string> GitAsync(string[] arguments) => RunGitAsync(Work, arguments);
        internal async Task<string> HeadAsync() => (await GitAsync(["rev-parse", "HEAD"])).Trim();
        internal async Task<string> RemoteHeadAsync() => (await RunGitAsync(_root,
            ["--git-dir=origin.git", "rev-parse", "refs/heads/codex/generated"])).Trim();
        internal async Task ReleaseLeaseAsync()
        {
            if (_lease is not null) await _lease.DisposeAsync();
            _lease = null;
        }
        private async Task<string> RunGitAsync(string root, string[] arguments)
        {
            var result = await _git.RunAsync(root, arguments, null, CancellationToken.None);
            Assert.IsTrue(result.IsSuccess, result.StandardError);
            return result.StandardOutput;
        }
        public async ValueTask DisposeAsync()
        {
            if (_lease is not null) await _lease.DisposeAsync();
            if (Directory.Exists(_root))
            {
                foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
                Directory.Delete(_root, recursive: true);
            }
        }
    }

    private sealed class FaultRunner(ICodeGenerationGitCommandRunner inner) : ICodeGenerationGitCommandRunner
    {
        internal bool RejectPush { get; set; }
        internal string? LoseReplyFor { get; set; }
        internal List<IReadOnlyList<string>> Commands { get; } = [];
        public async Task<GitCommandResult> RunAsync(string root, IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? configuration, CancellationToken token)
        {
            Commands.Add(arguments.ToArray());
            if (RejectPush && arguments[0] == "push") return new(1, "", "rejected");
            var result = await inner.RunAsync(root, arguments, configuration, token);
            if (LoseReplyFor is { } operation && arguments.Contains(operation) && result.IsSuccess)
            {
                LoseReplyFor = null;
                return new(1, "", "response lost");
            }
            return result;
        }
    }
}
