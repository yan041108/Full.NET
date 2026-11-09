using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Host.Worker;

/// <summary>在独立宿主内保留正式报表、文件和数据库边界，仅注入外部数据及进程强退检查点。</summary>
internal static class ReportingRecoveryFixture
{
    internal static bool TryRegister(IServiceCollection services, IConfiguration configuration,
        HashSet<ServiceDescriptor> existingHostedServices)
    {
        var mode = configuration["RecoveryFixture:Pause"];
        if (mode is not ("reporting-upload" or "reporting-recovery")) return false;

        foreach (var descriptor in services.Where(item => item.ServiceType == typeof(IHostedService)
            && !existingHostedServices.Contains(item)
            && item.ImplementationType?.Name != "ReportingExportTaskHostedProcessor").ToArray())
            services.Remove(descriptor);

        var marker = configuration["RecoveryFixture:GateFile"] ?? throw new InvalidOperationException("缺少报表恢复标记。");
        var taskId = Guid.Parse(configuration["RecoveryFixture:DataTaskId"] ?? throw new InvalidOperationException("缺少报表任务标识。"));
        services.Replace(ServiceDescriptor.Singleton<IExternalDatabaseConnectionFactory>(new ExternalRows(marker + ".queries")));
        var original = services.Single(item => item.ServiceType == typeof(ITenantResourceFileStore));
        services.Remove(original);
        services.AddScoped<ITenantResourceFileStore>(provider => new UploadGate(
            (ITenantResourceFileStore)ActivatorUtilities.CreateInstance(provider, original.ImplementationType!),
            taskId, marker, mode == "reporting-upload"));
        return true;
    }

    private sealed class ExternalRows(string queries) : IExternalDatabaseConnectionFactory, IExternalDatabaseSession
    {
        public Task<ExternalDatabaseSessionResult> OpenAsync(ExternalDatabaseConnectionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExternalDatabaseSessionResult(true, null, this));

        public Task<ExternalDatabaseScalarResult> ExecuteScalarAsync(string sql, int commandTimeoutSeconds, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExternalDatabaseScalarResult(true, null, 1));

        public async Task<ExternalDatabaseQueryResult> ExecuteQueryAsync(string sql, IReadOnlyDictionary<string, object?> parameters,
            int commandTimeoutSeconds, int maxCellValueLength, CancellationToken cancellationToken = default)
        {
            await File.AppendAllTextAsync(queries, "query" + Environment.NewLine, cancellationToken);
            return new(true, null, ["SchemaName"], [new Dictionary<string, string?> { ["SchemaName"] = "recovery fixture" }]);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class UploadGate(ITenantResourceFileStore original, Guid taskId, string marker, bool pause) : ITenantResourceFileStore
    {
        public async Task<Result<TenantResourceFileReference>> UploadAsync(string ownerModuleKey, Guid resourceId, Guid actorUserId,
            string originalFileName, string contentType, Stream content, long contentLength, CancellationToken cancellationToken = default)
        {
            var result = await original.UploadAsync(ownerModuleKey, resourceId, actorUserId, originalFileName, contentType, content,
                contentLength, cancellationToken);
            if (ownerModuleKey == "reporting" && resourceId == taskId && result.IsSuccess)
            {
                // 只有 Files 已持久化对象并标记就绪后才允许父进程终止 Worker。
                await File.WriteAllTextAsync(marker + ".tmp", result.Value!.FileId.ToString("D"), cancellationToken);
                File.Move(marker + ".tmp", marker);
                if (pause) await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            return result;
        }

        public Task<Result<TenantResourceFileContent>> OpenReadyContentAsync(string ownerModuleKey, Guid resourceId, Guid fileId,
            CancellationToken cancellationToken = default) => original.OpenReadyContentAsync(ownerModuleKey, resourceId, fileId, cancellationToken);

        public Task<IReadOnlyList<TenantResourceFileReadyItem>> ListReadyAsync(string ownerModuleKey, Guid resourceId,
            CancellationToken cancellationToken = default) => original.ListReadyAsync(ownerModuleKey, resourceId, cancellationToken);

        public Task ReleaseAsync(string ownerModuleKey, Guid resourceId, Guid fileId, CancellationToken cancellationToken = default) =>
            original.ReleaseAsync(ownerModuleKey, resourceId, fileId, cancellationToken);
    }
}
