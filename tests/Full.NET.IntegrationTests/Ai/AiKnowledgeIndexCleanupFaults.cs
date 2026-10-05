using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>SQL 仍真实执行；在删除后模拟进程中断，在 Files 窄边界模拟释放失败。</summary>
internal sealed class AiKnowledgeIndexCleanupFaults
{
    internal bool InterruptCleanup;
    internal bool FailFiles;
    internal CancellationTokenSource? CancelCleanup;
    internal int Interrupted;
    internal int Releases;
    internal Guid VersionId;
    internal Guid? TenantId;
    internal SqlStatement? VectorStatement;
    internal Dictionary<string, object?>? VectorParameters;
    internal TaskCompletionSource<bool>? VectorEntering;
    internal TaskCompletionSource<bool>? VectorWritten;
    internal TaskCompletionSource<bool>? ContinueVector;
    internal TaskCompletionSource<bool>? CleanupEntering;
    internal TaskCompletionSource<bool>? CleanupLocked;
    internal TaskCompletionSource<bool>? ContinueCleanup;
    internal TaskCompletionSource<bool>? CandidatesReady;
    internal int CandidateReaders;

    internal void Configure(IServiceCollection services)
    {
        var command = services.Last(x => x.ServiceType == typeof(ICommandExecutor));
        var query = services.Last(x => x.ServiceType == typeof(IQueryExecutor));
        services.RemoveAll<ICommandExecutor>();
        services.AddScoped<ICommandExecutor>(provider => new Commands((ICommandExecutor)(command.ImplementationInstance
            ?? command.ImplementationFactory?.Invoke(provider) ?? ActivatorUtilities.CreateInstance(provider, command.ImplementationType!)), this));
        services.RemoveAll<IQueryExecutor>();
        services.AddScoped<IQueryExecutor>(provider => new Queries((IQueryExecutor)(query.ImplementationInstance
            ?? query.ImplementationFactory?.Invoke(provider) ?? ActivatorUtilities.CreateInstance(provider, query.ImplementationType!)), this));
        services.RemoveAll<IHostFileReferenceClaimService>(); services.RemoveAll<ITenantResourceFileCleanup>();
        services.AddScoped<IHostFileReferenceClaimService>(provider => new Files(this, provider.GetRequiredService<IDataTransactionState>(), provider.GetRequiredService<ICurrentTenant>()));
        services.AddScoped<ITenantResourceFileCleanup>(provider => new Files(this, provider.GetRequiredService<IDataTransactionState>(), provider.GetRequiredService<ICurrentTenant>()));
    }

    private sealed class Commands(ICommandExecutor inner, AiKnowledgeIndexCleanupFaults faults) : ICommandExecutor
    {
        public async Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            var vector = statement.Name.StartsWith("ai.knowledge_index_vector.insert", StringComparison.Ordinal);
            if (vector)
            {
                faults.VectorStatement = statement;
                faults.VectorParameters = new((IReadOnlyDictionary<string, object?>)parameters!);
                faults.VectorEntering?.TrySetResult(true);
            }
            var changed = await inner.ExecuteAsync(statement, parameters, cancellationToken);
            if (changed > 0 && statement.Name.StartsWith("ai.knowledge_index_cleanup.vectors_", StringComparison.Ordinal)) faults.CancelCleanup?.Cancel();
            if (vector && changed == 1 && faults.VectorWritten is { } written)
            {
                written.TrySetResult(true);
                await faults.ContinueVector!.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            if (changed == 1 && statement.Name.StartsWith("ai.knowledge_index_cleanup.cancel_", StringComparison.Ordinal) && faults.CleanupLocked is { } locked)
            {
                locked.TrySetResult(true);
                await faults.ContinueCleanup!.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            if (faults.InterruptCleanup && changed > 0 && statement.Name.StartsWith("ai.knowledge_index_cleanup.vectors_", StringComparison.Ordinal))
            {
                faults.Interrupted++;
                throw new IOException("Simulated interruption after deleting vectors.");
            }
            return changed;
        }
    }

    private sealed class Queries(IQueryExecutor inner, AiKnowledgeIndexCleanupFaults faults) : IQueryExecutor
    {
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            if (statement.Name.StartsWith("ai.knowledge_index_cleanup.lock_", StringComparison.Ordinal)) faults.CleanupEntering?.TrySetResult(true);
            return inner.QuerySingleOrDefaultAsync<T>(statement, parameters, cancellationToken);
        }
        public async Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            var rows = await inner.QueryAsync<T>(statement, parameters, cancellationToken);
            if (statement.Name.StartsWith("ai.knowledge_index_cleanup.list_", StringComparison.Ordinal) && faults.CandidatesReady is { } ready)
            {
                if (Interlocked.Increment(ref faults.CandidateReaders) == 2) ready.TrySetResult(true);
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return rows;
        }
    }

    private sealed class Files(AiKnowledgeIndexCleanupFaults faults, IDataTransactionState transactions, ICurrentTenant tenant)
        : IHostFileReferenceClaimService, ITenantResourceFileCleanup
    {
        private void Release(CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Assert.IsFalse(transactions.HasTransaction);
            Assert.AreEqual(faults.TenantId, tenant.Id); faults.Releases++;
            if (faults.FailFiles) throw new IOException("Simulated file release failure.");
        }
        public Task<Result<bool>> ReleaseAsync(string idempotencyKey, CancellationToken cancellationToken = default)
        {
            Assert.IsTrue(tenant.IsHost);
            Assert.AreEqual(HostFileReferenceClaimIdempotencyKeys.AiKnowledgeVersion(faults.VersionId), idempotencyKey);
            Release(cancellationToken); return Task.FromResult(Result<bool>.Success(true));
        }
        public Task<bool> ReleaseOwnedAsync(string ownerModuleKey, Guid resourceId, CancellationToken cancellationToken = default)
        {
            Assert.AreEqual("ai", ownerModuleKey); Assert.AreEqual(faults.VersionId, resourceId);
            Assert.IsFalse(tenant.IsHost); Release(cancellationToken); return Task.FromResult(true);
        }
        public Task<Result<HostFileReferenceClaimResult>> ClaimAsync(HostFileReferenceClaimRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<HostFileReferenceClaimResult>> ConfirmAsync(string idempotencyKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasOpenClaimsAsync(Guid fileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
