using Full.NET.Abstractions.Results;
using Full.NET.Modules.CodeGeneration.Configuration;
using Full.NET.Modules.CodeGeneration.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.CodeGeneration.Git;

/// <summary>
/// 在 Apply/Rollback 前同步本地工作区到远程分支，并在变更后提交推送；凭据从环境变量读取并作为 http.extraHeader 注入，禁止写入配置文件。
/// </summary>
internal sealed class CodeGenerationGitWorkspaceService(
    IOptions<CodeGenerationGitOptions> gitOptions,
    IOptions<CodeGenerationApplyOptions> applyOptions,
    ICodeGenerationGitCommandRunner commandRunner,
    ILogger<CodeGenerationGitWorkspaceService> logger)
{
    /// <summary>Git 与 Apply 同时启用时视为激活；未激活时同步与发布均为空操作。</summary>
    public bool IsActive =>
        gitOptions.Value.Enabled && applyOptions.Value.Enabled;

    /// <summary>
    /// 在工作区变更前执行 fetch + reset --hard 到远程分支，确保起点干净；任一步失败返回 GitSyncFailed，调用方必须中止后续 Apply/Rollback。
    /// </summary>
    /// <remarks>reset --hard 是破坏性操作，仅因工作区由代码生成独占写而安全；外部人工改动会被丢弃，不应在工作区与其他流程共享时启用。</remarks>
    /// <returns>成功返回 null；失败返回 GitSyncFailed 错误。</returns>
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

    /// <summary>
    /// 在工作区变更后按 status/add -A/commit/push 顺序发布；任一步失败仅记录 GitPublishFailed 警告不抛异常，避免回滚已成功的工作区变更。
    /// PushEnabled 关闭或无 porcelain 变更时为空操作。
    /// </summary>
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