using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Platform.Contracts;
using Full.NET.Modules.Platform.Persistence;

namespace Full.NET.Modules.Platform.Features.ManageBackupExecutor;

/// <summary>
/// 在配置产物根目录内解析已成功运行的备份产物，拒绝路径穿越与符号链接。
/// </summary>
internal sealed class BackupRunArtifactService(
    IQueryExecutor queryExecutor,
    BackupExecutorStatusService statusService)
{
    /// <summary>打开受控下载流；不满足条件时返回稳定业务错误。</summary>
    /// <param name="runId">运行标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>下载句柄或错误。</returns>
    public async Task<Result<BackupRunDownload>> OpenDownloadAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<BackupRunRecord>(
                BackupExecutorSql.FindRunById,
                BackupExecutorSqlParameters.Create(("Id", runId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return Failure(
                PlatformErrorCodes.BackupRunNotFound,
                "The backup run was not found.",
                ErrorType.NotFound);
        }

        if (!string.Equals(record.Status, BackupRunStatuses.Succeeded, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(record.ArtifactFileName))
        {
            return Failure(
                PlatformErrorCodes.BackupRunDownloadUnavailable,
                "Only succeeded backup runs with artifacts can be downloaded.",
                ErrorType.Validation);
        }

        if (!IsSafeArtifactFileName(record.ArtifactFileName))
        {
            return Failure(
                PlatformErrorCodes.BackupRunDownloadUnavailable,
                "The backup artifact file name is not allowed.",
                ErrorType.Validation);
        }

        var resolved = TryResolveArtifactPath(record.TaskKey, record.ArtifactFileName);
        if (resolved is null)
        {
            return Failure(
                PlatformErrorCodes.BackupRunDownloadUnavailable,
                "The backup artifact is not available on this host.",
                ErrorType.NotFound);
        }

        try
        {
            var file = new FileInfo(resolved);
            if (!file.Exists
                || (file.Attributes & FileAttributes.ReparsePoint) != 0
                || File.ResolveLinkTarget(resolved, returnFinalTarget: false) is not null)
            {
                return Failure(
                    PlatformErrorCodes.BackupRunDownloadUnavailable,
                    "The backup artifact is not available on this host.",
                    ErrorType.NotFound);
            }

            var stream = file.OpenRead();
            var contentType = string.IsNullOrWhiteSpace(record.ArtifactContentType)
                ? "application/octet-stream"
                : record.ArtifactContentType;
            return Result<BackupRunDownload>.Success(
                new BackupRunDownload(
                    stream,
                    file.Name,
                    contentType,
                    file.Length,
                    file.LastWriteTimeUtc));
        }
        catch (IOException)
        {
            return Failure(
                PlatformErrorCodes.BackupRunDownloadUnavailable,
                "The backup artifact is not available on this host.",
                ErrorType.NotFound);
        }
        catch (UnauthorizedAccessException)
        {
            return Failure(
                PlatformErrorCodes.BackupRunDownloadUnavailable,
                "The backup artifact is not available on this host.",
                ErrorType.NotFound);
        }
    }

    /// <summary>校验产物文件名仅包含单层文件名，禁止目录分段与穿越。</summary>
    /// <param name="artifactFileName">持久化文件名。</param>
    /// <returns>是否安全。</returns>
    internal static bool IsSafeArtifactFileName(string artifactFileName)
    {
        if (string.IsNullOrWhiteSpace(artifactFileName))
        {
            return false;
        }

        if (artifactFileName.Contains('/', StringComparison.Ordinal)
            || artifactFileName.Contains('\\', StringComparison.Ordinal)
            || artifactFileName.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        return string.Equals(
            Path.GetFileName(artifactFileName),
            artifactFileName,
            StringComparison.Ordinal);
    }

    private string? TryResolveArtifactPath(string taskKey, string artifactFileName)
    {
        if (string.IsNullOrWhiteSpace(taskKey)
            || taskKey.Contains('/', StringComparison.Ordinal)
            || taskKey.Contains('\\', StringComparison.Ordinal)
            || taskKey.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        var root = statusService.ResolveArtifactRootPath();
        if (!Directory.Exists(root))
        {
            return null;
        }

        var taskDirectory = Path.GetFullPath(Path.Combine(root, taskKey));
        if (!taskDirectory.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var candidate = Path.GetFullPath(Path.Combine(taskDirectory, artifactFileName));
        if (!candidate.StartsWith(taskDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return candidate;
    }

    private static Result<BackupRunDownload> Failure(
        string code,
        string message,
        ErrorType type) =>
        Result<BackupRunDownload>.Failure(new Error(code, message, type));
}

/// <summary>受控下载句柄，调用方负责释放流。</summary>
/// <param name="Content">只读内容流。</param>
/// <param name="FileName">下载文件名。</param>
/// <param name="ContentType">内容类型。</param>
/// <param name="SizeBytes">文件大小。</param>
/// <param name="LastModifiedUtc">最后修改时间（UTC）。</param>
internal sealed record BackupRunDownload(
    Stream Content,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTimeOffset LastModifiedUtc);
