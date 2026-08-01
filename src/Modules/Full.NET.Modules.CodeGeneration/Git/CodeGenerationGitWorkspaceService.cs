using Full.NET.Abstractions.Results;
using Full.NET.Modules.CodeGeneration.Configuration;
using Full.NET.Modules.CodeGeneration.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.CodeGeneration.Git;

internal sealed class CodeGenerationGitWorkspaceService(
    IOptions<CodeGenerationGitOptions> gitOptions,
    IOptions<CodeGenerationApplyOptions> applyOptions,
    ICodeGenerationGitCommandRunner commandRunner,
    ILogger<CodeGenerationGitWorkspaceService> logger)
{
    public bool IsActive =>
        gitOptions.Value.Enabled && applyOptions.Value.Enabled;

    public async Task<Error?> SynchronizeAsync(CancellationToken cancellationToken)
    {
        if (!IsActive)
        {
            return null;
        }

        var workspaceRoot = applyOptions.Value.WorkspaceRoot;
        if (!IsGitRepository(workspaceRoot))
        {
            return SyncFailed("The code generation workspace is not a git repository.");
        }

        var configuration = BuildCredentialConfiguration();
        var remote = gitOptions.Value.RemoteName;
        var branch = gitOptions.Value.DefaultBranch;
        var fetch = await commandRunner.RunAsync(
                workspaceRoot,
                ["fetch", remote, branch],
                configuration,
                cancellationToken)
            .ConfigureAwait(false);
        if (!fetch.IsSuccess)
        {
            return SyncFailed("Git fetch failed before code generation mutation.");
        }

        var reset = await commandRunner.RunAsync(
                workspaceRoot,
                ["reset", "--hard", $"{remote}/{branch}"],
                configuration,
                cancellationToken)
            .ConfigureAwait(false);
        if (!reset.IsSuccess)
        {
            return SyncFailed("Git reset failed before code generation mutation.");
        }

        return null;
    }

    public async Task PublishAsync(
        string commitMessage,
        CancellationToken cancellationToken)
    {
        if (!IsActive || !gitOptions.Value.PushEnabled)
        {
            return;
        }

        var workspaceRoot = applyOptions.Value.WorkspaceRoot;
        if (!IsGitRepository(workspaceRoot))
        {
            logger.LogWarning(
                "Skipping code generation git publish because the workspace is not a repository.");
            return;
        }

        var configuration = BuildCredentialConfiguration();
        var status = await commandRunner.RunAsync(
                workspaceRoot,
                ["status", "--porcelain"],
                configuration,
                cancellationToken)
            .ConfigureAwait(false);
        if (!status.IsSuccess)
        {
            LogPublishFailure("Git status failed after code generation mutation.");
            return;
        }

        if (string.IsNullOrWhiteSpace(status.StandardOutput))
        {
            return;
        }

        var author = gitOptions.Value;
        var add = await commandRunner.RunAsync(
                workspaceRoot,
                ["add", "-A"],
                configuration,
                cancellationToken)
            .ConfigureAwait(false);
        if (!add.IsSuccess)
        {
            LogPublishFailure("Git add failed after code generation mutation.");
            return;
        }

        var commit = await commandRunner.RunAsync(
                workspaceRoot,
                [
                    "-c",
                    $"user.name={author.AuthorName}",
                    "-c",
                    $"user.email={author.AuthorEmail}",
                    "commit",
                    "-m",
                    commitMessage,
                ],
                configuration,
                cancellationToken)
            .ConfigureAwait(false);
        if (!commit.IsSuccess)
        {
            LogPublishFailure("Git commit failed after code generation mutation.");
            return;
        }

        var push = await commandRunner.RunAsync(
                workspaceRoot,
                ["push", author.RemoteName, $"HEAD:{author.DefaultBranch}"],
                configuration,
                cancellationToken)
            .ConfigureAwait(false);
        if (!push.IsSuccess)
        {
            LogPublishFailure("Git push failed after code generation mutation.");
        }
    }

    private IReadOnlyDictionary<string, string>? BuildCredentialConfiguration()
    {
        var variable = gitOptions.Value.CredentialEnvironmentVariable;
        var token = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        return new Dictionary<string, string>
        {
            ["http.extraHeader"] = $"Authorization: Bearer {token}",
        };
    }

    private static bool IsGitRepository(string workspaceRoot)
    {
        var gitDirectory = Path.Combine(
            workspaceRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar),
            ".git");
        return Directory.Exists(gitDirectory);
    }

    private static Error SyncFailed(string message) =>
        new(
            CodeGenerationRunErrorCodes.GitSyncFailed,
            message,
            ErrorType.Conflict);

    private void LogPublishFailure(string message) =>
        logger.LogWarning(
            "{Message} ErrorCode={ErrorCode}",
            message,
            CodeGenerationRunErrorCodes.GitPublishFailed);
}