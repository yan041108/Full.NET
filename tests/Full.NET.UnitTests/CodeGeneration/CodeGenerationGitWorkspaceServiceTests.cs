using Full.NET.Modules.CodeGeneration.Configuration;
using Full.NET.Modules.CodeGeneration.Contracts;
using Full.NET.Modules.CodeGeneration.Git;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Full.NET.Data.CodeGeneration.Generation;

namespace Full.NET.UnitTests.CodeGeneration;

/// <summary>单元测试（代码生成）：Code Generation Git Workspace Service。</summary>
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
    public async Task Synchronize_inspects_the_working_branch_without_resetting_it()
    {
        using var workspace = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(workspace.Path, ".git"));
        var runner = new RecordingGitCommandRunner();
        const string sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        runner.Enqueue(new(0, "codex/test\n", ""));
        runner.Enqueue(GitCommandResultTestExtensions.Success());
        runner.Enqueue(GitCommandResultTestExtensions.Success());
        runner.Enqueue(new(0, sha + "\n", ""));
        runner.Enqueue(GitCommandResultTestExtensions.Success());
        runner.Enqueue(new(0, sha + "\trefs/heads/codex/test\n", ""));
        var service = CreateService(
            workspace.Path,
            runner,
            enabled: true,
            pushEnabled: false);

        var error = await service.SynchronizeAsync(CancellationToken.None);

        Assert.IsNull(error);
        Assert.AreEqual(6, runner.Invocations.Count);
        CollectionAssert.AreEqual(
            new[] { "fetch", "origin" },
            runner.Invocations[4].Arguments.ToArray());
        Assert.IsFalse(runner.Invocations.Any(invocation => invocation.Arguments.Contains("reset")));
    }

    [TestMethod]
    public async Task Publish_skips_when_git_is_disabled()
    {
        using var workspace = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(workspace.Path, ".git"));
        var runner = new RecordingGitCommandRunner();
        var service = CreateService(
            workspace.Path,
            runner,
            enabled: false,
            pushEnabled: false);

        await service.PublishAsync(Guid.NewGuid(), "codegen(apply): test",
            GenerationWritePlanner.Plan([], new Dictionary<string, string>()), CancellationToken.None);

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
                WorkingBranch = "codex/test",
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
