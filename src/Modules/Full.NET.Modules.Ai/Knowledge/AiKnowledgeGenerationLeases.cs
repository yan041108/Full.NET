using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>完整召回从载荷读取到解码结束共用一组资产引用，不持有数据库事务等待正文 IO。</summary>
internal sealed class AiKnowledgeGenerationLeases(IQueryExecutor query, ICommandExecutor command, ICommandTransaction transaction,
    ICurrentTenant tenant, IClock clock, IIdGenerator ids, IOptions<DatabaseOptions> database)
{
    internal async Task<LeaseSet> AcquireAsync(IReadOnlyList<AiKnowledgeIndexStatusRecord> sources, Guid actor, CancellationToken token)
    {
        if (sources.Count is < 1 or > 8) throw new InvalidDataException("Invalid publication source count.");
        var result = new LeaseSet();
        try
        {
            foreach (var source in sources.OrderBy(source => source.DocumentId))
            {
                if (source.PublicationId is null) throw new InvalidDataException("Retrieval requires a publication.");
                var lease = await AiKnowledgeGenerationLease.AcquireAsync(source, actor, query, command, transaction, tenant, clock, ids, database, token).ConfigureAwait(false)
                    ?? throw new InvalidDataException("Publication asset is unavailable.");
                result.Items.Add(lease);
            }
            result.EnsureValid(); return result;
        }
        catch { await result.DisposeAsync().ConfigureAwait(false); throw; }
    }
    internal sealed class LeaseSet : IAsyncDisposable
    {
        internal List<AiKnowledgeGenerationLease> Items { get; } = [];
        internal void EnsureValid()
        { if (Items.Any(item => !item.IsValid)) throw new OperationCanceledException("Publication read lease expired."); }
        public async ValueTask DisposeAsync()
        { foreach (var item in Items) await item.DisposeAsync().ConfigureAwait(false); Items.Clear(); }
    }
}
