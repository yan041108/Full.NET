using Full.NET.Modules.CodeGeneration.Configuration;
using Full.NET.Modules.CodeGeneration.Contracts;
using Full.NET.Modules.CodeGeneration.Git;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class CodeGenerationGitWorkspaceServiceTests
{
    [TestMethod]
    public async Task Synchronize_returns_error_when_git_enabled_but_workspace_is_not_repository()
    {
        using var workspace = new TemporaryDirectory();
        var runner = new RecordingGitCommandRunner();
        var service = CreateService(
            workspace.Path,
            runner,
            enabled: true,
            pushEnabled: false);

        var error = await service.SynchronizeAsync(CancellationToken.None);

        Assert.IsNotNull(error);
        Assert.AreEqual(CodeGenerationRunErrorCodes.GitSyncFailed, error!.Code);
        Assert.AreEqual(0, runner.Invocations.Count);
    }

    [TestMethod]
    public async Task Synchronize_runs_fetch_and_reset_when_repository_exists()
    {
        using var workspace = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(workspace.Path, ".git"));
        var runner = new RecordingGitCommandRunner();
        runner.Enqueue(GitCommandResultTestExtensions.Success());
        runner.Enqueue(GitCommandResultTestExtensions.Success());
        var service = CreateService(
            workspace.Path,
            runner,
            enabled: true,
            pushEnabled: false);

        var error = await service.SynchronizeAsync(CancellationToken.None);

        Assert.IsNull(error);
        Assert.AreEqual(2, runner.Invocations.Count);
        CollectionAssert.AreEqual(
            new[] { "fetch", "origin", "main" },
            runner.Invocations[0].Arguments.ToArray());
        CollectionAssert.AreEqual(
            new[] { "reset", "--hard", "origin/main" },
            runner.Invocations[1].Arguments.ToArray());
    }

    [TestMethod]
    public async Task Publish_skips_when_push_disabled()
    {
        using var workspace = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(workspace.Path, ".git"));
        var runner = new RecordingGitCommandRunner();
        var service = CreateService(
            workspace.Path,
            runner,
            enabled: true,
            pushEnabled: false);

        await service.PublishAsync("codegen(apply): test", CancellationToken.None);

        Assert.AreEqual(0, runner.Invocations.Count);
    }

    private static CodeGenerationGitWorkspaceService CreateService(
        string workspaceRoot,
        RecordingGitCommandRunner runner,
        bool enabled,
        bool pushEnabled) =>
        new(
            Options.Create(new CodeGenerationGitOptions
            {
                Enabled = enabled,
                PushEnabled = pushEnabled,
                AuthorName = "Full.NET",
                AuthorEmail = "codegen@fullnet.local",
            }),
            Options.Create(new CodeGenerationApplyOptions
            {
                Enabled = true,
                WorkspaceRoot = workspaceRoot,
            }),
            runner,
            NullLogger<CodeGenerationGitWorkspaceService>.Instance);

    internal sealed class RecordingGitCommandRunner : ICodeGenerationGitCommandRunner
    {
        private readonly Queue<GitCommandResult> _results = new();

        public List<GitInvocation> Invocations { get; } = [];

        public void Enqueue(GitCommandResult result) => _results.Enqueue(result);

        public Task<GitCommandResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? configuration,
            CancellationToken cancellationToken)
        {
            Invocations.Add(new GitInvocation(workingDirectory, arguments));
            if (_results.Count == 0)
            {
                return Task.FromResult(GitCommandResultTestExtensions.Success());
            }

            return Task.FromResult(_results.Dequeue());
        }
    }

    internal sealed record GitInvocation(
        string WorkingDirectory,
        IReadOnlyList<string> Arguments);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = Directory.CreateTempSubdirectory(
                "fullnet-codegeneration-git-").FullName;
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

internal static class GitCommandResultTestExtensions
{
    public static GitCommandResult Success() => new(0, string.Empty, string.Empty);
}