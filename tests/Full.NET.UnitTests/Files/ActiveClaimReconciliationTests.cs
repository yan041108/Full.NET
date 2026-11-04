using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.HostFileReferenceClaims;
using Full.NET.Modules.Files.Reconciliation;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Files;

/// <summary>历史不可复用引用的 Active 对账必须保守释放，不能依赖已丢失的删除事件。</summary>
[TestClass]
public sealed class ActiveClaimReconciliationTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Missing_document_version_releases_stale_active_claim(DatabaseProvider provider)
    {
        var now = new DateTimeOffset(2026, 10, 13, 8, 0, 0, TimeSpan.Zero);
        var record = new HostFileReferenceClaimRecord(Guid.CreateVersion7(), "historical-active", Guid.CreateVersion7(),
            HostFileReferenceClaimConsumerModules.Document, Guid.CreateVersion7(), HostFileReferenceClaimStates.Active,
            null, 1, now.AddHours(-2), now.AddHours(-1), now.AddHours(-1), null);
        // 内部版本记录不授予代理程序集访问；手写查询替身保持生产封装和真实探测路径。
        var queries = new TestQueries(record);
        var probeType = typeof(DocumentModule).Assembly.GetType(
            "Full.NET.Modules.Document.Features.HostFileReferences.HostDocumentVersionReferenceProbe", throwOnError: true)!;
        var probe = (IHostFileReferenceClaimProbe)Activator.CreateInstance(probeType, queries)!;
        var commands = Substitute.For<ICommandExecutor>();
        SqlStatement? release = null;
        Dictionary<string, object?>? parameters = null;
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            release = call.ArgAt<SqlStatement>(0);
            parameters = (Dictionary<string, object?>)call.ArgAt<object>(1);
            return Task.FromResult(1);
        });
        var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(now);
        // 首个 RED 通过已存在的真实 Document 探测进入缺失能力；测试反射不进入生产闭包。
        var assembly = typeof(PendingHostFileReferenceClaimReconciliationRunner).Assembly;
        var runnerType = assembly.GetType("Full.NET.Modules.Files.Reconciliation.ActiveHostFileReferenceClaimReconciliationRunner");
        Assert.IsNotNull(runnerType, "Active orphan reconciliation has not been implemented.");
        var optionsType = assembly.GetType("Full.NET.Modules.Files.Reconciliation.ActiveHostFileReferenceClaimReconciliationOptions", true)!;
        var options = Activator.CreateInstance(optionsType)!;
        optionsType.GetProperty("MaxBatchesPerRun")!.SetValue(options, 1);
        var runner = Activator.CreateInstance(runnerType, queries, commands, new[] { probe },
            Options.Create(new DatabaseOptions { Provider = provider }), clock, null)!;
        var task = (Task)runnerType.GetMethod("RunOnceAsync")!.Invoke(runner, [options, CancellationToken.None])!;
        await task;
        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        Assert.AreEqual(1, result.GetType().GetProperty("Released")!.GetValue(result));
        Assert.IsNotNull(release); Assert.IsNotNull(parameters);
        StringAssert.Contains(release.Text, "State = @ActiveState");
        StringAssert.Contains(release.Text, "UpdatedAtUtc = @ObservedUpdatedAtUtc");
        StringAssert.Contains(release.Text, "FileId = @ObservedFileId");
        StringAssert.Contains(release.Text, "ConsumerReferenceId = @ObservedConsumerReferenceId");
        Assert.AreEqual(record.UpdatedAtUtc, parameters["ObservedUpdatedAtUtc"]);
        Assert.AreEqual(SqlDataScope.HostOnly, release.Scope);
    }

    private sealed class TestQueries(HostFileReferenceClaimRecord record) : IQueryExecutor
    {
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<T>>(typeof(T) == typeof(HostFileReferenceClaimRecord) ? new[] { (T)(object)record } : []);
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(default(T));
    }
}
