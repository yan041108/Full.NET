using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.ImportExport.Features.ManageImportTasks;
using Full.NET.Modules.ImportExport.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;
using CommonCodes = Full.NET.Abstractions.Results.CommonErrorCodes;

namespace Full.NET.UnitTests.ImportExport;

/// <summary>状态与 Schema 输入在 DB 前校验，迟到取消不能映射出成功页。</summary>
[TestClass]
public sealed class ImportTaskStatusFilterTests
{
    [TestMethod] [DataRow("unknown")] [DataRow("EXECUTION_FAILED")] [DataRow("queued' OR 1=1")]
    public async Task Invalid_status_never_reaches_database(string status)
    {
        var queries = Substitute.For<IQueryExecutor>(); var multiple = Substitute.For<IMultiResultQueryExecutor>();
        var result = await Service(queries, multiple).ListAsync(1, 20, null, status);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(CommonCodes.RequestInvalid, result.Error!.Code); Assert.AreEqual(ErrorType.Validation, result.Error.Type);
        Assert.AreEqual(0, queries.ReceivedCalls().Count()); Assert.AreEqual(0, multiple.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Oversized_schema_is_rejected_without_database()
    {
        var queries = Substitute.For<IQueryExecutor>(); var multiple = Substitute.For<IMultiResultQueryExecutor>();
        var result = await Service(queries, multiple).ListAsync(1, 20, new string('x', 129));
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(CommonCodes.RequestInvalid, result.Error!.Code);
        Assert.AreEqual(0, queries.ReceivedCalls().Count()); Assert.AreEqual(0, multiple.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Already_cancelled_query_does_not_call_database()
    {
        using var source = new CancellationTokenSource(); source.Cancel();
        var queries = Substitute.For<IQueryExecutor>(); var multiple = Substitute.For<IMultiResultQueryExecutor>();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => Service(queries, multiple).ListAsync(1, 20, null, cancellationToken: source.Token));
        Assert.AreEqual(0, queries.ReceivedCalls().Count()); Assert.AreEqual(0, multiple.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Ignored_database_cancellation_cannot_return_a_success_page()
    {
        using var source = new CancellationTokenSource(); var multiple = Substitute.For<IMultiResultQueryExecutor>();
        multiple.QueryMultipleAsync(Arg.Any<SqlStatement>(), Arg.Any<object>(),
            Arg.Any<Func<IMultiResultReader, CancellationToken, Task<(long Total, IReadOnlyList<ImportExportTaskRecord> Rows)>>>(), Arg.Any<CancellationToken>())
            .Returns(_ => { source.Cancel(); return (Total: 0L, Rows: (IReadOnlyList<ImportExportTaskRecord>)Array.Empty<ImportExportTaskRecord>()); });
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => Service(Substitute.For<IQueryExecutor>(), multiple).ListAsync(1, 20, null, cancellationToken: source.Token));
    }

    private static ImportExportTaskQueryService Service(IQueryExecutor queries, IMultiResultQueryExecutor multiple) =>
        new(queries, multiple, Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }), Substitute.For<ICurrentTenant>(), Substitute.For<IClock>());
}
