using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageChatSessions;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>真实来源守卫与可回滚事务夹具验证标题写入和历史交付的边界。</summary>
[TestClass]
public sealed class AiChatRenameTransactionTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Http_rename_reads_knowledge_history_only_after_commit(bool tenantScope)
    {
        var f = new Fixture(tenantScope);
        var result = await f.Service.RenameAsync(f.SessionId, f.OwnerId, new(" updated ", 1));
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("updated", result.Value!.Title);
        Assert.AreEqual(2, result.Value.Version);
        Assert.AreEqual(1, result.Value.Messages.Count);
        Assert.AreEqual("", result.Value.Messages[0].Content);
        Assert.AreEqual(AiErrorCodes.KnowledgeAnswerUnavailable, result.Value.Messages[0].KnowledgeUnavailableCode);
        Assert.AreEqual(0, f.HistoryReadsInTransaction);
        Assert.AreEqual(1, f.Commits);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Tool_rename_returns_mutation_result_without_reading_history(bool tenantScope)
    {
        var f = new Fixture(tenantScope);
        var result = await f.ExecuteResultAsync(token => f.Service.RenameInCurrentTransactionAsync(f.SessionId, f.OwnerId, new("updated", 1), token), default);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(0, f.HistoryReads);
        Assert.AreEqual("updated", f.Title);
        Assert.AreEqual(2, f.Version);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Version_conflict_rolls_back_and_does_not_read_history(bool tool)
    {
        var f = new Fixture();
        if (tool)
        {
            var result = await f.ExecuteResultAsync(token => f.Service.RenameInCurrentTransactionAsync(f.SessionId, f.OwnerId, new("updated", 99), token), default);
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(AiErrorCodes.ChatSessionConcurrencyConflict, result.Error!.Code);
        }
        else
        {
            var result = await f.Service.RenameAsync(f.SessionId, f.OwnerId, new("updated", 99));
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(AiErrorCodes.ChatSessionConcurrencyConflict, result.Error!.Code);
        }
        Assert.AreEqual(0, f.Commits);
        Assert.AreEqual(0, f.HistoryReads);
        Assert.AreEqual("old", f.Title);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Cancellation_after_driver_returns_rolls_back_title(bool tool)
    {
        var f = new Fixture(); using var cancellation = new CancellationTokenSource();
        f.AfterWrite = cancellation.Cancel;
        if (tool) await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await f.ExecuteResultAsync(token => f.Service.RenameInCurrentTransactionAsync(f.SessionId, f.OwnerId, new("updated", 1), token), cancellation.Token));
        else await Assert.ThrowsAsync<OperationCanceledException>(() => f.Service.RenameAsync(f.SessionId, f.OwnerId, new("updated", 1), cancellation.Token));
        Assert.AreEqual("old", f.Title);
        Assert.AreEqual(1, f.Version);
        Assert.AreEqual(0, f.Commits);
        Assert.AreEqual(0, f.HistoryReads);
    }

    [TestMethod]
    public async Task Scope_change_during_title_write_rolls_back_without_releasing_history()
    {
        var f = new Fixture(); f.AfterWrite = () => f.Tenant.SetTenant(new(Guid.CreateVersion7(), "other", "其他"));
        var result = await f.Service.RenameAsync(f.SessionId, f.OwnerId, new("updated", 1));
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual("old", f.Title);
        Assert.AreEqual(0, f.Commits);
        Assert.AreEqual(0, f.HistoryReads);
    }

    [TestMethod]
    public async Task Scope_change_after_commit_does_not_query_new_scope()
    {
        var f = new Fixture(); f.AfterCommit = () => f.Tenant.SetTenant(new(Guid.CreateVersion7(), "other", "其他"));
        var result = await f.Service.RenameAsync(f.SessionId, f.OwnerId, new("updated", 1));
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(0, f.DetailReads);
        Assert.AreEqual(1, f.Commits);
    }

    [TestMethod]
    public async Task Cancellation_after_commit_does_not_read_history()
    {
        var f = new Fixture(); using var cancellation = new CancellationTokenSource(); f.AfterCommit = cancellation.Cancel;
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Service.RenameAsync(f.SessionId, f.OwnerId, new("updated", 1), cancellation.Token));
        Assert.AreEqual(1, f.Commits);
        Assert.AreEqual("updated", f.Title);
        Assert.AreEqual(0, f.HistoryReads);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow(null)]
    public async Task Invalid_title_is_rejected_before_database_write(string? title)
    {
        var f = new Fixture();
        var result = await f.Service.RenameAsync(f.SessionId, f.OwnerId, new(title!, 1));
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(AiErrorCodes.ChatSessionInvalid, result.Error!.Code);
        Assert.AreEqual(0, f.Writes);
        Assert.AreEqual(0, f.HistoryReads);
    }

    /// <summary>故意忽略 SQL 取消；事务只按真实委托结果回滚，不代替调用方检查取消。</summary>
    private sealed class Fixture : IQueryExecutor, ICommandExecutor, ICommandTransaction, IDataTransactionState
    {
        internal readonly Guid SessionId = Guid.CreateVersion7(), OwnerId = Guid.CreateVersion7();
        internal readonly CurrentTenantAccessor Tenant = new();
        internal readonly AiChatSessionManagementService Service;
        internal readonly Guid? OriginalTenantId;
        internal string Title = "old";
        internal int Version = 1, Commits, HistoryReads, HistoryReadsInTransaction, DetailReads, Writes;
        internal Action? AfterWrite, AfterCommit;
        public bool HasTransaction { get; private set; }

        internal Fixture(bool tenantScope = false)
        {
            if (tenantScope) Tenant.SetTenant(new(Guid.CreateVersion7(), "tenant", "租户")); else Tenant.SetHost();
            OriginalTenantId = Tenant.Id;
            var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
            var options = Options.Create(new DatabaseOptions());
            var guard = new AiKnowledgeAnswerHistoryGuard(this, this, Tenant, options, null!, null!, null!);
            var queries = new AiChatSessionQueryService(this, Tenant, options, clock, guard);
            Service = new(this, this, this, queries, null!, Tenant, clock, Substitute.For<IIdGenerator>());
        }

        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken token = default)
        {
            DetailReads++;
            var values = (IReadOnlyDictionary<string, object?>)parameters!;
            if (!Equals(values["ScopeTenantId"], OriginalTenantId) || !Equals(values["OwnerUserId"], OwnerId)) return Task.FromResult<T?>(default);
            return Task.FromResult<T?>((T)(object)new AiChatSessionRecord { Id = SessionId, OwnerUserId = OwnerId, TenantId = OriginalTenantId, Title = Title, Version = Version });
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken token = default)
        {
            HistoryReads++; if (HasTransaction) HistoryReadsInTransaction++;
            return Task.FromResult<IReadOnlyList<T>>([(T)(object)new AiChatMessageRecord { Id = Guid.CreateVersion7(), SessionId = SessionId,
                RoleKey = "assistant", StatusKey = "completed", IsKnowledgeAnswer = true, KnowledgeLineage = "invalid", Content = "不可交付的旧答案" }]);
        }

        public Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken token = default)
        {
            Assert.IsTrue(HasTransaction); Assert.AreEqual(AiChatSql.UpdateSessionTitle, statement); Writes++;
            var values = (IReadOnlyDictionary<string, object?>)parameters!;
            if (!Equals(values["Version"], Version) || !Equals(values["ScopeTenantId"], OriginalTenantId) || !Equals(values["OwnerUserId"], OwnerId)) return Task.FromResult(0);
            Title = (string)values["Title"]!; Version++; AfterWrite?.Invoke(); return Task.FromResult(1);
        }

        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken token)
        {
            var before = (Title, Version); HasTransaction = true;
            try { var value = await action(token); Commits++; return value; }
            catch { (Title, Version) = before; throw; }
            finally { HasTransaction = false; AfterCommit?.Invoke(); }
        }

        public async Task<Result<T>> ExecuteResultAsync<T>(Func<CancellationToken, Task<Result<T>>> action, CancellationToken token)
        {
            var before = (Title, Version); HasTransaction = true;
            try { var value = await action(token); if (value.IsSuccess) Commits++; else (Title, Version) = before; return value; }
            catch { (Title, Version) = before; throw; }
            finally { HasTransaction = false; AfterCommit?.Invoke(); }
        }
    }
}
