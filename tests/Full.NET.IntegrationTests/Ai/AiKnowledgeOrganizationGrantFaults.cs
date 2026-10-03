using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实租户流打开后撤权，以及第二条授予写入故障，保留生产事务与存储。</summary>
internal sealed class AiKnowledgeOrganizationGrantFaults
{
    internal Func<Task>? AfterOpen;
    internal Stream? Opened;
    internal int FailInsertAt;
    internal bool FailAuthorityRead;
    internal bool CancelAuthorityRead;
    private int inserts;
    internal void Configure(IServiceCollection services)
    {
        var content = services.Last(x => x.ServiceType == typeof(ITenantResourceFileStore));
        var command = services.Last(x => x.ServiceType == typeof(ICommandExecutor));
        var directory = services.Last(x => x.ServiceType == typeof(ICurrentTenantOrganizationUnitDirectory));
        services.RemoveAll<ITenantResourceFileStore>(); services.RemoveAll<ICommandExecutor>();
        services.AddScoped<ITenantResourceFileStore>(p => new Content((ITenantResourceFileStore)Resolve(p, content), this));
        services.AddScoped<ICommandExecutor>(p => new Commands((ICommandExecutor)Resolve(p, command), this));
        services.RemoveAll<ICurrentTenantOrganizationUnitDirectory>();
        services.AddScoped<ICurrentTenantOrganizationUnitDirectory>(p => new Directory((ICurrentTenantOrganizationUnitDirectory)Resolve(p, directory), this));
    }
    private static object Resolve(IServiceProvider p, ServiceDescriptor d) => d.ImplementationInstance ?? d.ImplementationFactory?.Invoke(p) ?? ActivatorUtilities.CreateInstance(p, d.ImplementationType!);
    private sealed class Directory(ICurrentTenantOrganizationUnitDirectory inner, AiKnowledgeOrganizationGrantFaults faults) : ICurrentTenantOrganizationUnitDirectory
    {
        public Task<PagedResult<TenantOrganizationUnitDirectoryEntry>> ListActiveUnitsAsync(int page, int pageSize, CancellationToken cancellationToken = default) => inner.ListActiveUnitsAsync(page, pageSize, cancellationToken);
        public Task<IReadOnlyDictionary<Guid, TenantOrganizationUnitDirectoryEntry>> FindActiveUnitsAsync(IReadOnlyCollection<Guid> unitIds, CancellationToken cancellationToken = default) => inner.FindActiveUnitsAsync(unitIds, cancellationToken);
        public Task<IReadOnlyList<Guid>> ListActiveUnitIdsForUserAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            if (faults.FailAuthorityRead) { faults.FailAuthorityRead = false; throw new InvalidOperationException("Injected authority read failure."); }
            if (faults.CancelAuthorityRead) { faults.CancelAuthorityRead = false; return Task.FromCanceled<IReadOnlyList<Guid>>(new CancellationToken(true)); }
            return inner.ListActiveUnitIdsForUserAsync(userId, cancellationToken);
        }
    }
    private sealed class Content(ITenantResourceFileStore inner, AiKnowledgeOrganizationGrantFaults faults) : ITenantResourceFileStore
    {
        public Task<Result<TenantResourceFileReference>> UploadAsync(string ownerModuleKey, Guid resourceId, Guid actorUserId, string originalFileName, string contentType, Stream content, long contentLength, CancellationToken cancellationToken = default) => inner.UploadAsync(ownerModuleKey, resourceId, actorUserId, originalFileName, contentType, content, contentLength, cancellationToken);
        public Task<IReadOnlyList<TenantResourceFileReadyItem>> ListReadyAsync(string ownerModuleKey, Guid resourceId, CancellationToken cancellationToken = default) => inner.ListReadyAsync(ownerModuleKey, resourceId, cancellationToken);
        public Task ReleaseAsync(string ownerModuleKey, Guid resourceId, Guid fileId, CancellationToken cancellationToken = default) => inner.ReleaseAsync(ownerModuleKey, resourceId, fileId, cancellationToken);
        public async Task<Result<TenantResourceFileContent>> OpenReadyContentAsync(string ownerModuleKey, Guid resourceId, Guid fileId, CancellationToken cancellationToken = default)
        {
            var result = await inner.OpenReadyContentAsync(ownerModuleKey, resourceId, fileId, cancellationToken);
            if (result.IsSuccess && faults.AfterOpen is { } after) { faults.Opened = result.Value!.Content; faults.AfterOpen = null; await after(); }
            return result;
        }
    }
    private sealed class Commands(ICommandExecutor inner, AiKnowledgeOrganizationGrantFaults faults) : ICommandExecutor
    {
        public Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            if (faults.FailInsertAt > 0 && statement.Name.StartsWith("ai.knowledge_org.insert", StringComparison.Ordinal) && ++faults.inserts == faults.FailInsertAt)
            { faults.FailInsertAt = 0; faults.inserts = 0; return Task.FromResult(0); }
            return inner.ExecuteAsync(statement, parameters, cancellationToken);
        }
    }
}
