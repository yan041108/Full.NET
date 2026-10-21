using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Configuration;
using Full.NET.Modules.Document.Features.ManageHostDocumentItems;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.Document.Retention;
using Full.NET.Modules.Files.Contracts;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Document;

/// <summary>保留预算限制实际版本尝试，不能按文档项计数或让最小保留规则制造毒头项。</summary>
[TestClass]
public sealed class DocumentVersionRetentionBudgetTests
{
    [TestMethod]
    [DataRow(1, 1)]
    [DataRow(4, 3)]
    public async Task Failed_attempts_share_version_budget_and_respect_effective_retention(int minimum, int retained)
    {
        var queries = new BudgetQueries(retained);
        var options = new DocumentVersionRetentionOptions { BatchSize = 3, MinimumRetainedVersionsPerItem = minimum, MaximumRetainedHistoryVersions = 1 };
        var monitor = Substitute.For<IOptionsMonitor<DocumentVersionRetentionOptions>>(); monitor.CurrentValue.Returns(options);
        var deletion = new DocumentVersionDeletionService(queries, Substitute.For<ICommandExecutor>(), new PassThroughTransaction(),
            Substitute.For<IHostFileReferenceClaimService>(), monitor, Substitute.For<IClock>(), Substitute.For<IIdGenerator>(), new TestDocumentAccessPolicy());
        var runner = new DocumentVersionRetentionRunner(queries, deletion, Options.Create(new DatabaseOptions()));
        var result = await runner.RunOnceAsync(options, CancellationToken.None);
        Assert.AreEqual(0, result.VersionsDeleted);
        Assert.AreEqual(3, queries.Attempts);
    }

    private sealed class BudgetQueries(int retained) : IQueryExecutor
    {
        internal int Attempts;
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            var values = (IReadOnlyDictionary<string, object?>)parameters!;
            if (typeof(T) == typeof(DocumentVersionRetentionCandidateRecord))
            {
                Assert.AreEqual(retained, values["MaximumRetainedHistoryVersions"]);
                return Task.FromResult((IReadOnlyList<T>)(object)new[] { new DocumentVersionRetentionCandidateRecord {
                    DocumentItemId = Guid.NewGuid(), CurrentVersionId = Guid.NewGuid(), HistoryCount = 10_000 } });
            }
            if (typeof(T) == typeof(DocumentVersionRecord))
            {
                var take = (int)values["TakeCount"]!; Assert.IsLessThanOrEqualTo(3, take);
                return Task.FromResult((IReadOnlyList<T>)(object)Enumerable.Range(0, take).Select(_ => new DocumentVersionRecord { Id = Guid.NewGuid() }).ToArray());
            }
            throw new InvalidOperationException(statement.Name);
        }
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            Assert.AreEqual(DocumentItemSql.FindActiveById, statement); Attempts++;
            return Task.FromResult(default(T));
        }
    }

    private sealed class PassThroughTransaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken) => action(cancellationToken);
    }
}
