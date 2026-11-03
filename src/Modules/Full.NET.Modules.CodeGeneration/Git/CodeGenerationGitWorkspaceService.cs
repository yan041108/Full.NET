using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Modules.CodeGeneration.Configuration;
using Full.NET.Modules.CodeGeneration.Contracts;
using Full.NET.Modules.CodeGeneration.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.CodeGeneration.Git;

/// <summary>沿显式工作分支提交计划内文件，先持久化本地提交再发布，绝不重置或强推。</summary>
internal sealed class CodeGenerationGitWorkspaceService(
    IOptions<CodeGenerationGitOptions> gitOptions,
    IOptions<CodeGenerationApplyOptions> applyOptions,
    ICodeGenerationGitCommandRunner commandRunner,
    ILogger<CodeGenerationGitWorkspaceService> logger)
{
    private const string CurrentPublication = ".fullnet/codegeneration-git-publication.json";
    private string? _synchronizedHead;
    private string? _remoteHead;

    public bool IsActive => gitOptions.Value.Enabled && applyOptions.Value.Enabled;

    /// <summary>核对干净工作分支与远端基线；有待发布提交时先重试同一提交，不改变本地源码。</summary>
    public async Task<Error?> SynchronizeAsync(CancellationToken cancellationToken)
    {
        try { return await SynchronizeCoreAsync(cancellationToken); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return SyncFailed("The saved publication requires recovery before further workspace mutation.");
        }
    }

    private async Task<Error?> SynchronizeCoreAsync(CancellationToken cancellationToken)
    {
        if (!IsActive) return null;
        var options = gitOptions.Value;
        if (string.IsNullOrWhiteSpace(options.WorkingBranch) || options.WorkingBranch == options.DefaultBranch)
            return SyncFailed("A dedicated working branch must be configured.");
        if (!Directory.Exists(Path.Combine(applyOptions.Value.WorkspaceRoot, ".git"))
            && !File.Exists(Path.Combine(applyOptions.Value.WorkspaceRoot, ".git")))
            return SyncFailed("The workspace is not a git repository.");
        var branch = await RunAsync(["symbolic-ref", "--quiet", "--short", "HEAD"], cancellationToken);
        if (!branch.IsSuccess || branch.StandardOutput.Trim() != options.WorkingBranch)
            return SyncFailed("The configured working branch must already be checked out.");
        var previous = await ReadCurrentAsync(cancellationToken);
        if (previous is not null && previous.Status is "prepared" or "pending")
        {
            var retried = await ResumeAsync(previous, cancellationToken);
            if (retried.Status is not ("published" or "local_only"))
                return SyncFailed("The previous local operation still requires publication or recovery.");
            previous = await ReadCurrentAsync(cancellationToken);
        }
        var status = await RunAsync(["status", "--porcelain", "-z", "--untracked-files=all", "--", ".", ":(exclude).fullnet"], cancellationToken);
        var staged = await RunAsync(["diff", "--cached", "--name-only", "-z"], cancellationToken);
        if (!status.IsSuccess || !staged.IsSuccess || status.StandardOutput.Length != 0 || staged.StandardOutput.Length != 0)
            return SyncFailed("The dedicated workspace contains changes outside a pending operation.");
        var head = await HeadAsync(cancellationToken);
        if (head is null || (previous is not null && previous.LocalCommitSha != head))
            return SyncFailed("The local commit chain has drifted.");
        var fetch = await RunAsync(["fetch", options.RemoteName], cancellationToken);
        if (!fetch.IsSuccess) return SyncFailed("Git fetch failed before workspace mutation.");
        var remote = await RemoteHeadAsync(cancellationToken);
        if (!remote.Success) return SyncFailed("The remote working branch could not be inspected.");
        var baseline = remote.Sha;
        if (baseline is null)
        {
            var initial = await RunAsync(["rev-parse", options.RemoteName + "/" + options.DefaultBranch], cancellationToken);
            if (!initial.IsSuccess || !IsSha(initial.StandardOutput.Trim())) return SyncFailed("The initial branch baseline is unavailable.");
            baseline = initial.StandardOutput.Trim();
        }
        if (head != baseline)
        {
            var ancestor = await RunAsync(["merge-base", "--is-ancestor", baseline, head], cancellationToken);
            if (previous is null || !ancestor.IsSuccess || options.PushEnabled)
                return SyncFailed("Local and remote commits require explicit reconciliation; no reset was performed.");
        }
        _synchronizedHead = head;
        _remoteHead = remote.Sha;
        return null;
    }

    /// <summary>保存计划允许的后态和基线，只提交这些路径；文件成功与发布状态分别返回。</summary>
    public async Task<CodeGenerationGitPublicationResult> PublishAsync(Guid runId, string message,
        GenerationWritePlan plan, CancellationToken cancellationToken)
    {
        if (IsActive && (_synchronizedHead is null || plan.NextManifest is null))
            return Pending(null, "Publication has no reviewed baseline.");
        return await PublishFrozenAsync(CapturePublicationContext(runId, message, plan), cancellationToken);
    }

    internal string CapturePublicationContext(Guid runId, string message, GenerationWritePlan plan)
    {
        if (plan.NextManifest is null) throw new ArgumentException("发布必须使用无冲突完整计划。", nameof(plan));
        if (IsActive && _synchronizedHead is null) throw new InvalidOperationException("尚未验证 Git 基线。");
        var files = plan.Actions.Select(action => new CodeGenerationGitPublicationFile(action.RelativePath, action.DesiredSha256))
            .Append(new(GenerationWorkspaceStore.ManifestRelativePath, GenerationContentHash.Compute(plan.NextManifest.ToJson()))).ToArray();
        return JsonSerializer.Serialize(new CodeGenerationGitPublication(runId, message,
            IsActive ? gitOptions.Value.WorkingBranch : string.Empty, IsActive ? _synchronizedHead! : string.Empty, IsActive ? _remoteHead : null, null,
            IsActive ? "prepared" : "disabled", files), CodeGenerationJsonSerializerContext.Default.CodeGenerationGitPublication);
    }

    internal bool MatchesPublicationBaseline(string recoveryContext)
    {
        var frozen = ParsePublication(recoveryContext);
        return frozen is not null && (frozen.Status == "disabled" ? !IsActive
            : IsActive && frozen.Branch == gitOptions.Value.WorkingBranch && frozen.BaselineSha == _synchronizedHead && frozen.ExpectedRemoteSha == _remoteHead);
    }

    internal async Task<CodeGenerationGitPublicationResult> PublishFrozenAsync(string recoveryContext, CancellationToken cancellationToken)
    {
        var frozen = ParsePublication(recoveryContext);
        var runId = frozen.RunId;
        if (frozen.Status == "disabled")
        {
            try { await SaveHistoricalAsync(frozen, cancellationToken); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return new("unavailable", null); }
            return new("disabled", null);
        }
        if (!IsActive || frozen.Branch != gitOptions.Value.WorkingBranch || frozen.Status != "prepared")
            return Pending(null, "Publication configuration differs from the frozen operation.");
        try
        {
            var state = await ReadCurrentAsync(cancellationToken);
            if (state?.RunId == runId)
            {
                if (state.Message != frozen.Message || state.Branch != frozen.Branch || state.BaselineSha != frozen.BaselineSha
                    || state.ExpectedRemoteSha != frozen.ExpectedRemoteSha
                    || !state.Files.Select(file => (file.Path, file.Sha256)).SequenceEqual(frozen.Files.Select(file => (file.Path, file.Sha256))))
                    return Pending(state.LocalCommitSha, "Publication evidence differs from the frozen operation.");
            }
            else
            {
                state = frozen;
                await SaveAsync(state, cancellationToken);
            }
            return Remember(await ResumeAsync(state, cancellationToken));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return Pending(await HeadAsync(cancellationToken), "Publication evidence could not be finalized; the local state is retained.");
        }
    }

    /// <summary>发布重试只使用已冻结的提交和文件摘要，不重新生成或重做 Apply。</summary>
    public async Task<CodeGenerationGitPublicationResult> RetryPublicationAsync(Guid runId, CancellationToken cancellationToken)
    {
        if (!IsActive) return new("disabled", null);
        try
        {
            var state = await ReadCurrentAsync(cancellationToken);
            return state is null || state.RunId != runId ? Pending(null, "The publication record is unavailable.")
                : Remember(await ResumeAsync(state, cancellationToken));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { return Pending(null, "The saved publication requires review; no source was regenerated."); }
    }

    /// <summary>只读取当次收据；配置变化、记录缺失或损坏时不把历史状态猜测为已发布。</summary>
    public async Task<CodeGenerationGitPublicationResult> GetSavedPublicationAsync(Guid runId, CancellationToken token)
    {
        if (!Directory.Exists(applyOptions.Value.WorkspaceRoot)) return new("unavailable", null);
        try
        {
            var json = await GenerationWorkspaceMetadata.ReadAsync(applyOptions.Value.WorkspaceRoot,
                $".fullnet/codegeneration-git-publications/{runId:N}.json", token);
            var state = json is null ? null : ParsePublication(json);
            if (state is null || state.RunId != runId || state.Status is not ("prepared" or "pending" or "published" or "local_only" or "disabled")
                || state.LocalCommitSha is not null && !IsSha(state.LocalCommitSha)
                || state.Status is "published" or "local_only" && state.LocalCommitSha is null)
                return new("unavailable", null);
            return new(state.Status == "prepared" ? "pending" : state.Status, state.LocalCommitSha);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return new("unavailable", null);
        }
    }

    private async Task<CodeGenerationGitPublicationResult> ResumeAsync(CodeGenerationGitPublication state, CancellationToken token)
    {
        var branch = await RunAsync(["symbolic-ref", "--quiet", "--short", "HEAD"], token);
        if (!branch.IsSuccess || branch.StandardOutput.Trim() != gitOptions.Value.WorkingBranch)
            return Pending(state.LocalCommitSha, "The dedicated working branch is no longer checked out.");
        if (state.RunId == Guid.Empty || state.Branch != gitOptions.Value.WorkingBranch || !IsSha(state.BaselineSha)
            || state.Files is null || state.Files.Select(file => file.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != state.Files.Length)
            return Pending(state.LocalCommitSha, "The publication record has drifted.");
        var head = await HeadAsync(token);
        if (head is null) return Pending(state.LocalCommitSha, "The local commit is unavailable.");
        var status = await RunAsync(["status", "--porcelain", "-z", "--untracked-files=all", "--", ".", ":(exclude).fullnet"], token);
        var staged = await RunAsync(["diff", "--cached", "--name-only", "-z"], token);
        var allowed = state.Files.Select(file => file.Path).ToHashSet(StringComparer.Ordinal);
        // 暂存区和工作树中的计划外文件都不进入提交；重命名的双路径形状保守拒绝。
        if (!status.IsSuccess || !staged.IsSuccess
            || status.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries).Any(line => line.Length < 4
                || line[..2].Contains('R') || line[..2].Contains('C') || !allowed.Contains(line[3..]))
            || staged.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries).Any(path => !allowed.Contains(path))
            || !await FilesMatchAsync(state.Files, token))
            return Pending(state.LocalCommitSha, "Files or the staging area differ from the reviewed publication.");
        if (state.LocalCommitSha is null)
        {
            if (head != state.BaselineSha)
            {
                // commit 响应丢失时只接受精确父提交、消息和受控文件树，不能认领任意新 HEAD。
                if (!await CommitMatchesAsync(head, state, token)) return Pending(null, "An unknown local commit requires recovery.");
            }
            else
            {
                var objects = new List<CodeGenerationGitPublicationFile>();
                foreach (var file in state.Files)
                {
                    if (file.Sha256 is null) { objects.Add(file); continue; }
                    var hashed = await RunAsync(["hash-object", "--path=" + file.Path, "--", file.Path], token);
                    if (!hashed.IsSuccess || !IsSha(hashed.StandardOutput.Trim()))
                        return Pending(null, "The reviewed Git object could not be computed.");
                    objects.Add(file with { GitObjectSha = hashed.StandardOutput.Trim() });
                }
                if (!await FilesMatchAsync(state.Files, token)) return Pending(null, "Files changed while Git objects were computed.");
                state = state with { Files = objects.ToArray() };
                // 保存 Git 规范化后的对象摘要，兼容仓库行尾规则，同时仍核对原始工作树摘要。
                await SaveAsync(state, token);
                var tracked = await RunAsync(["ls-files", "--cached", "-z"], token);
                if (!tracked.IsSuccess) return Pending(null, "Tracked files could not be inspected.");
                var trackedPaths = tracked.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
                var paths = state.Files.Where(file => file.Sha256 is not null || trackedPaths.Contains(file.Path))
                    .Select(file => ":(literal)" + file.Path).ToArray();
                var add = await RunAsync(["add", "-f", "--", .. paths], token);
                if (!add.IsSuccess) return Pending(null, "The reviewed files could not be staged.");
                var diff = await RunAsync(["diff", "--cached", "--name-only", "-z"], token);
                if (!diff.IsSuccess || diff.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries).Any(path => !allowed.Contains(path)))
                    return Pending(null, "The staging area contains unrelated files.");
                if (diff.StandardOutput.Length != 0)
                {
                    var author = gitOptions.Value;
                    _ = await RunAsync(["-c", "user.name=" + author.AuthorName, "-c", "user.email=" + author.AuthorEmail,
                        "commit", "--only", "-m", state.Message, "--", .. paths], token);
                    head = await HeadAsync(token);
                    if (head is null || !await CommitMatchesAsync(head, state, token))
                        return Pending(null, "The local commit could not be confirmed.");
                }
            }
            state = state with { LocalCommitSha = head, Status = "pending" };
            await SaveAsync(state, token);
        }
        if (head != state.LocalCommitSha) return Pending(state.LocalCommitSha, "HEAD no longer matches the saved local commit.");
        if (!gitOptions.Value.PushEnabled)
        {
            await SaveAsync(state with { Status = "local_only" }, token);
            return new("local_only", head);
        }
        var remote = await RemoteHeadAsync(token);
        if (!remote.Success) return Pending(head, "The remote commit could not be inspected.");
        if (remote.Sha != head)
        {
            if (remote.Sha != state.ExpectedRemoteSha) return Pending(head, "The remote branch has moved.");
            _ = await RunAsync(["push", gitOptions.Value.RemoteName, head + ":refs/heads/" + state.Branch], token);
            remote = await RemoteHeadAsync(token);
            if (!remote.Success || remote.Sha != head) return Pending(head, "The local commit remains pending publication.");
        }
        await SaveAsync(state with { Status = "published" }, token);
        return new("published", head);
    }

    private async Task<bool> FilesMatchAsync(CodeGenerationGitPublicationFile[] files, CancellationToken token)
    {
        if (files.Any(file => file.Path.Split('/').Any(segment => segment.Equals(".git", StringComparison.OrdinalIgnoreCase))
                || file.Path.StartsWith(".fullnet/", StringComparison.OrdinalIgnoreCase) && file.Path != GenerationWorkspaceStore.ManifestRelativePath
                || file.Sha256 is not null && !GenerationContentHash.IsValid(file.Sha256))) return false;
        var snapshot = await GenerationWorkspaceStore.CapturePathsAsync(applyOptions.Value.WorkspaceRoot,
            files.Where(file => file.Path != GenerationWorkspaceStore.ManifestRelativePath).Select(file => file.Path), token);
        foreach (var file in files)
        {
            var content = file.Path == GenerationWorkspaceStore.ManifestRelativePath
                ? snapshot.PreviousManifest?.ToJson() : snapshot.ExistingFiles.GetValueOrDefault(file.Path);
            if ((content is null ? null : GenerationContentHash.Compute(content)) != file.Sha256) return false;
        }
        return true;
    }

    private async Task<bool> CommitMatchesAsync(string sha, CodeGenerationGitPublication state, CancellationToken token)
    {
        var parent = await RunAsync(["rev-parse", sha + "^"], token);
        var message = await RunAsync(["log", "-1", "--format=%B", sha], token);
        var changed = await RunAsync(["diff-tree", "--no-commit-id", "--name-only", "-r", "-z", sha], token);
        var allowed = state.Files.Select(file => file.Path).ToHashSet(StringComparer.Ordinal);
        if (!parent.IsSuccess || parent.StandardOutput.Trim() != state.BaselineSha || !message.IsSuccess
            || message.StandardOutput.TrimEnd('\r', '\n') != state.Message || !changed.IsSuccess
            || changed.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries).Any(path => !allowed.Contains(path))) return false;
        foreach (var file in state.Files)
        {
            var blob = await RunAsync(["rev-parse", "--verify", sha + ":" + file.Path], token);
            if (file.Sha256 is null ? blob.IsSuccess : !blob.IsSuccess || blob.StandardOutput.Trim() != file.GitObjectSha)
                return false;
        }
        return await FilesMatchAsync(state.Files, token);
    }

    private async Task<CodeGenerationGitPublication?> ReadCurrentAsync(CancellationToken token)
    {
        var json = await GenerationWorkspaceMetadata.ReadAsync(applyOptions.Value.WorkspaceRoot, CurrentPublication, token);
        return json is null ? null : ParsePublication(json);
    }

    internal static CodeGenerationGitPublication ParsePublication(string json)
    {
        var state = JsonSerializer.Deserialize(json, CodeGenerationJsonSerializerContext.Default.CodeGenerationGitPublication);
        if (state is null || state.SchemaVersion != 1 || state.RunId == Guid.Empty || string.IsNullOrWhiteSpace(state.Message)
            || state.Message.Length > 1024 || state.Status is not ("prepared" or "pending" or "published" or "local_only" or "disabled")
            || state.Files is null || state.Files.Length is < 1 or > 2048
            || state.Files.Any(file => file is null) || state.Files.Select(file => file.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != state.Files.Length
            || state.LocalCommitSha is not null && !IsSha(state.LocalCommitSha)
            || state.ExpectedRemoteSha is not null && !IsSha(state.ExpectedRemoteSha)
            || state.Status is "published" or "local_only" && state.LocalCommitSha is null
            || state.Status != "disabled" && (string.IsNullOrWhiteSpace(state.Branch) || !IsSha(state.BaselineSha))
            || state.Status == "disabled" && (state.Branch != string.Empty || state.BaselineSha != string.Empty || state.LocalCommitSha is not null || state.ExpectedRemoteSha is not null))
            throw new JsonException("Git 发布记录结构或版本无效。");
        foreach (var file in state.Files)
        {
            GenerationArtifactPath.Validate(file.Path, nameof(json));
            if (file.Path.StartsWith(".fullnet/", StringComparison.OrdinalIgnoreCase) && file.Path != GenerationWorkspaceStore.ManifestRelativePath
                || file.Sha256 is not null && !GenerationContentHash.IsValid(file.Sha256)
                || file.GitObjectSha is not null && !IsSha(file.GitObjectSha)) throw new JsonException("Git 发布路径或摘要无效。");
        }
        if (!state.Files.Any(file => file.Path == GenerationWorkspaceStore.ManifestRelativePath && file.Sha256 is not null))
            throw new JsonException("Git 发布缺少最终清单。");
        return state;
    }

    private async Task SaveAsync(CodeGenerationGitPublication state, CancellationToken token)
    {
        var json = JsonSerializer.Serialize(state, CodeGenerationJsonSerializerContext.Default.CodeGenerationGitPublication);
        await GenerationWorkspaceMetadata.WriteAsync(applyOptions.Value.WorkspaceRoot, CurrentPublication, json, token);
        await SaveHistoricalAsync(state, token);
    }

    private Task SaveHistoricalAsync(CodeGenerationGitPublication state, CancellationToken token) =>
        GenerationWorkspaceMetadata.WriteAsync(applyOptions.Value.WorkspaceRoot,
            $".fullnet/codegeneration-git-publications/{state.RunId:N}.json",
            JsonSerializer.Serialize(state, CodeGenerationJsonSerializerContext.Default.CodeGenerationGitPublication), token);

    private async Task<string?> HeadAsync(CancellationToken token)
    {
        var result = await RunAsync(["rev-parse", "HEAD"], token);
        return result.IsSuccess && IsSha(result.StandardOutput.Trim()) ? result.StandardOutput.Trim() : null;
    }

    private async Task<(bool Success, string? Sha)> RemoteHeadAsync(CancellationToken token)
    {
        var result = await RunAsync(["ls-remote", "--heads", gitOptions.Value.RemoteName, "refs/heads/" + gitOptions.Value.WorkingBranch], token);
        if (!result.IsSuccess) return (false, null);
        var text = result.StandardOutput.Trim();
        if (text.Length == 0) return (true, null);
        var parts = text.Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && IsSha(parts[0]) && parts[1] == "refs/heads/" + gitOptions.Value.WorkingBranch
            ? (true, parts[0]) : (false, null);
    }

    private async Task<GitCommandResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken token)
    {
        var credential = Environment.GetEnvironmentVariable(gitOptions.Value.CredentialEnvironmentVariable);
        IReadOnlyDictionary<string, string>? configuration = string.IsNullOrWhiteSpace(credential) ? null
            : new Dictionary<string, string> { ["http.extraHeader"] = "Authorization: Bearer " + credential };
        try { return await commandRunner.RunAsync(applyOptions.Value.WorkspaceRoot, arguments, configuration, token); }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            return new(-1, string.Empty, "The local Git tool is unavailable.");
        }
    }

    private CodeGenerationGitPublicationResult Remember(CodeGenerationGitPublicationResult result)
    {
        if (result.Status is "published" or "local_only") _synchronizedHead = result.CommitSha;
        if (result.Status == "published") _remoteHead = result.CommitSha;
        return result;
    }

    private static bool IsSha(string? text) => text is { Length: 40 or 64 } && text.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static Error SyncFailed(string message) => new(CodeGenerationRunErrorCodes.GitSyncFailed, message, ErrorType.Conflict);
    private CodeGenerationGitPublicationResult Pending(string? sha, string message)
    {
        logger.LogWarning("{Message} ErrorCode={Code}", message, CodeGenerationRunErrorCodes.GitPublishFailed);
        return new("pending", sha);
    }
}
