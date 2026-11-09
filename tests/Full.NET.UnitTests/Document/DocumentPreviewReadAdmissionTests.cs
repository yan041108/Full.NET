using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Features.ManageHostDocumentPreviewTasks;

namespace Full.NET.UnitTests.Document;

/// <summary>首次不存在的任务不能绕过 ACL 后在第二次读取中变成可访问任务。</summary>
[TestClass]
public sealed class DocumentPreviewReadAdmissionTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Missing_task_fails_closed_before_any_unguarded_second_read(bool output)
    {
        var queries = new MissingOnceQueries();
        var service = new HostDocumentPreviewTaskQueryService(queries, null!, null!, null!, null!, null!, null!);
        var error = output ? (await service.OpenOutputContentAsync(Guid.CreateVersion7())).Error
            : (await service.GetAsync(Guid.CreateVersion7())).Error;
        Assert.IsNotNull(error); Assert.AreEqual(Full.NET.Modules.Document.Contracts.DocumentErrorCodes.PreviewTaskNotFound, error.Code);
        Assert.AreEqual(1, queries.Reads);
    }

    [TestMethod]
    [DataRow("pending")]
    [DataRow("processing")]
    public async Task Unready_snapshot_does_not_mix_with_concurrent_success(string status)
    {
        var taskId = Guid.CreateVersion7();
        var queries = new CompletingQueries(taskId, status);
        // 本次快照未成功就不能进入存储或成功日志，依赖为空可确保没有隐藏副作用。
        var service = new HostDocumentPreviewTaskQueryService(queries, null!, null!, null!, new TestDocumentAccessPolicy(), null!, null!);
        var result = await service.OpenOutputContentAsync(taskId);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(Full.NET.Modules.Document.Contracts.DocumentErrorCodes.PreviewTaskNotReady, result.Error!.Code);
        Assert.AreEqual(1, queries.Reads, "不能为同一请求把进行中的身份与完成后的输出拼接。");
    }

    private sealed class CompletingQueries(Guid id, string status) : IQueryExecutor
    {
        internal int Reads { get; private set; }
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            if (typeof(T) != typeof(DocumentPreviewTaskRecord)) throw new InvalidOperationException("Unready task must not read content metadata.");
            var record = new DocumentPreviewTaskRecord { Id = id, DocumentItemId = id,
                StatusKey = ++Reads == 1 ? status : HostDocumentPreviewTaskStatusKeys.Succeeded,
                OutputFileId = Reads == 1 ? null : id };
            return Task.FromResult((T?)(object)record);
        }
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Unexpected list read.");
    }

    private sealed class MissingOnceQueries : IQueryExecutor
    {
        internal int Reads { get; private set; }
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            if (++Reads > 1) throw new InvalidOperationException("An unguarded second read could observe a concurrently created task.");
            return Task.FromResult<T?>(default);
        }
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Missing task must stop before further reads.");
    }
}
