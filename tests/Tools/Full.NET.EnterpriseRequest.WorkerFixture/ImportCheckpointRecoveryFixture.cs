using Full.NET.Data.Abstractions;

namespace Full.NET.Host.Worker;

/// <summary>保留真实导入循环，在业务已提交且尚未写任务检查点时暂停独立进程。</summary>
internal static class ImportCheckpointRecoveryFixture
{
    internal static bool TryRegister(IServiceCollection services, IConfiguration configuration,
        HashSet<ServiceDescriptor> existingHostedServices)
    {
        var mode = configuration["RecoveryFixture:Pause"];
        if (mode is not ("import-checkpoint" or "import-recovery")) return false;
        foreach (var descriptor in services.Where(item => item.ServiceType == typeof(IHostedService)
            && !existingHostedServices.Contains(item)
            && item.ImplementationType?.Name != "ImportExportTaskHostedProcessor").ToArray())
            services.Remove(descriptor);

        if (mode == "import-checkpoint")
        {
            var marker = configuration["RecoveryFixture:GateFile"] ?? throw new InvalidOperationException("缺少导入检查点标记。");
            var taskId = Guid.Parse(configuration["RecoveryFixture:DataTaskId"] ?? throw new InvalidOperationException("缺少导入任务标识。"));
            var original = services.Single(item => item.ServiceType == typeof(ICommandExecutor));
            services.Remove(original);
            services.AddScoped<ICommandExecutor>(provider => new CheckpointGate(
                (ICommandExecutor)original.ImplementationFactory!(provider), taskId, marker));
        }
        return true;
    }

    private sealed class CheckpointGate(ICommandExecutor original, Guid taskId, string marker) : ICommandExecutor
    {
        public async Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            if (statement.Name == "import_export.task.update_execution_progress"
                && parameters is IReadOnlyDictionary<string, object?> values && Equals(values["Id"], taskId))
            {
                await File.WriteAllTextAsync(marker + ".tmp", taskId.ToString("D"), cancellationToken);
                File.Move(marker + ".tmp", marker);
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            return await original.ExecuteAsync(statement, parameters, cancellationToken);
        }
    }
}
