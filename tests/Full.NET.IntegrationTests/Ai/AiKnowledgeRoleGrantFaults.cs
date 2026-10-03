using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>仅在真实存储打开后及授予写入间隙注入故障，保留生产事务与查询实现。</summary>
internal sealed class AiKnowledgeRoleGrantFaults
{
    internal Func<Task>? AfterOpen;
    internal Stream? Opened;
    internal int FailRoleInsertAt;
    private int inserts;

    internal void Configure(IServiceCollection services)
    {
        var content = services.Last(item => item.ServiceType == typeof(IHostFileContentReader));
        var command = services.Last(item => item.ServiceType == typeof(ICommandExecutor));
        services.RemoveAll<IHostFileContentReader>();
        services.RemoveAll<ICommandExecutor>();
        services.AddScoped<IHostFileContentReader>(provider => new Content((IHostFileContentReader)Resolve(provider, content), this));
        services.AddScoped<ICommandExecutor>(provider => new Commands((ICommandExecutor)Resolve(provider, command), this));
    }
    private static object Resolve(IServiceProvider provider, ServiceDescriptor descriptor) => descriptor.ImplementationInstance
        ?? descriptor.ImplementationFactory?.Invoke(provider) ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!);

    private sealed class Content(IHostFileContentReader inner, AiKnowledgeRoleGrantFaults faults) : IHostFileContentReader
    {
        public async Task<Result<HostFileContent>> OpenReadyContentAsync(Guid fileId, CancellationToken cancellationToken = default)
        {
            var result = await inner.OpenReadyContentAsync(fileId, cancellationToken);
            if (result.IsSuccess && faults.AfterOpen is { } after)
            {
                faults.Opened = result.Value!.Content;
                faults.AfterOpen = null;
                await after();
            }
            return result;
        }
    }
    private sealed class Commands(ICommandExecutor inner, AiKnowledgeRoleGrantFaults faults) : ICommandExecutor
    {
        public Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            if (faults.FailRoleInsertAt > 0 && statement.Name.StartsWith("ai.knowledge_role.insert", StringComparison.Ordinal)
                && ++faults.inserts == faults.FailRoleInsertAt)
            { faults.FailRoleInsertAt = 0; faults.inserts = 0; return Task.FromResult(0); }
            return inner.ExecuteAsync(statement, parameters, cancellationToken);
        }
    }
}
