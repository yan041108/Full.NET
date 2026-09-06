using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Platform.Configuration;
using Full.NET.Modules.Platform.Contracts;
using Full.NET.Modules.Platform.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Platform.Features.ManageBackupExecutor;

/// <summary>读取授权备份执行器部署状态与产物根目录可达性。</summary>
internal sealed class BackupExecutorStatusService(
    IQueryExecutor queryExecutor,
    IOptions<PlatformBackupExecutorOptions> options,
    IHostEnvironment environment)
{
    /// <summary>汇总执行器配置、产物根目录与已启用任务数量。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>部署状态快照。</returns>
    public async Task<BackupExecutorStatusResponse> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        var enabledTaskCount = await queryExecutor.QuerySingleOrDefaultAsync<int>(
                BackupExecutorSql.CountEnabledTasks,
                BackupExecutorSqlParameters.Create(),
                cancellationToken)
            .ConfigureAwait(false);
        var rootPath = ResolveArtifactRootPath();
        var rootExists = Directory.Exists(rootPath);
        return new BackupExecutorStatusResponse(
            options.Value.ArtifactRootPath,
            rootExists,
            enabledTaskCount,
            options.Value.DeploymentNotice);
    }

    internal string ResolveArtifactRootPath()
    {
        var configured = options.Value.ArtifactRootPath;
        return Path.GetFullPath(
            Path.IsPathRooted(configured)
                ? configured
                : Path.Combine(environment.ContentRootPath, configured));
    }
}
