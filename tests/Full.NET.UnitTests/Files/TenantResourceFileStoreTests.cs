using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.TenantResourceFiles;
using Full.NET.Modules.Files.Persistence;
using Full.NET.Modules.Files.Storage;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Files;

/// <summary>租户资源文件只能由同一租户、模块和资源访问，外发前必须存在独立提交的意图。</summary>
[TestClass]
public sealed class TenantResourceFileStoreTests
{
    /// <summary>隔离租户和资源所有权，并验证释放后不可继续读取。</summary>
    [TestMethod]
    public async Task Ownership_and_release_are_checked_before_storage_access()
    {
        var tenant = new CurrentTenantAccessor();
        var tenantA = new TenantContext(Guid.NewGuid(), "a", "A");
        tenant.SetTenant(tenantA);
        var queries = Substitute.For<IQueryExecutor>();
        var commands = Substitute.For<ICommandExecutor>();
        var storage = Substitute.For<IFileStorageProvider>();
        storage.ProviderKey.Returns("test");
        var resourceId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var ready = false;
        var inserted = false;
        var saves = 0;
        var content = new byte[] { 1, 2, 3 };
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var statement = call.ArgAt<SqlStatement>(0);
                SqlScopeGuard.Validate(statement, tenant);
                inserted |= statement == TenantResourceFileSql.Insert;
                if (statement == TenantResourceFileSql.MarkReady) ready = true;
                if (statement == TenantResourceFileSql.Release) ready = false;
                return 1;
            });
        storage.SaveAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Assert.IsTrue(inserted, "对象发布前必须登记所有权意图。");
                Assert.IsFalse(ready);
                saves++;
                return Task.CompletedTask;
            });
        storage.OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<Stream>(new MemoryStream(content)));
        queries.QuerySingleOrDefaultAsync<TenantResourceFileRecord>(
            Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                SqlScopeGuard.Validate(call.ArgAt<SqlStatement>(0), tenant);
                var parameters = (Dictionary<string, object?>)call.ArgAt<object>(1);
                return tenant.Id == tenantA.Id && (Guid)parameters["Id"]! == fileId
                    && (Guid)parameters["ResourceId"]! == resourceId
                    && (string)parameters["OwnerModuleKey"]! == "reporting"
                    ? new TenantResourceFileRecord(fileId, "report.xlsx", "application/test", 3, "hash", "test", "opaque", ready ? "ready" : "released")
                    : null;
            });
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(fileId);
        var store = Create(queries, commands, tenant, Substitute.For<IDataTransactionState>(), storage, ids);
        using var input = new MemoryStream(content);
        var upload = await store.UploadAsync("reporting", resourceId, Guid.NewGuid(), "report.xlsx", "application/test", input, 3);
        Assert.IsTrue(upload.IsSuccess);
        Assert.AreEqual(1, saves);

        var opened = await store.OpenReadyContentAsync("reporting", resourceId, fileId);
        Assert.IsTrue(opened.IsSuccess);
        await opened.Value!.Content.DisposeAsync();
        Assert.IsFalse((await store.OpenReadyContentAsync("reporting", Guid.NewGuid(), fileId)).IsSuccess);
        Assert.IsFalse((await store.OpenReadyContentAsync("import_export", resourceId, fileId)).IsSuccess);
        tenant.SetTenant(new TenantContext(Guid.NewGuid(), "b", "B"));
        Assert.IsFalse((await store.OpenReadyContentAsync("reporting", resourceId, fileId)).IsSuccess);
        tenant.SetTenant(tenantA);
        await store.ReleaseAsync("reporting", resourceId, fileId);
        Assert.IsFalse((await store.OpenReadyContentAsync("reporting", resourceId, fileId)).IsSuccess);
        await storage.Received(1).OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await storage.Received(1).DeleteAsync("opaque", Arg.Any<CancellationToken>());
    }

    /// <summary>存量文件回退必须以所属模块的真实引用确认为前提，不能仅凭 UUID。</summary>
    /// <param name="authorized">所属模块是否确认当前租户的引用。</param>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Legacy_file_requires_resource_owner_confirmation(bool authorized)
    {
        var tenant = new CurrentTenantAccessor();
        tenant.SetTenant(new TenantContext(Guid.NewGuid(), "a", "A"));
        var queries = Substitute.For<IQueryExecutor>();
        var storage = Substitute.For<IFileStorageProvider>();
        storage.ProviderKey.Returns("test");
        var resourceId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var owner = Substitute.For<ITenantResourceFileOwner>();
        owner.OwnerModuleKey.Returns("reporting");
        owner.IsReferencedAsync(resourceId, fileId, Arg.Any<CancellationToken>()).Returns(authorized);
        queries.QuerySingleOrDefaultAsync<TenantResourceFileRecord>(TenantResourceFileSql.FindAuthorizedLegacyReference,
            Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new TenantResourceFileRecord(fileId, "old.xlsx", "application/test", 1, "hash", "test", "legacy-key", "ready"));
        storage.OpenReadAsync("legacy-key", Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<Stream>(new MemoryStream([1])));
        var state = Substitute.For<IDataTransactionState>();
        var store = new TenantResourceFileStore(queries, Substitute.For<ICommandExecutor>(), tenant, state,
            new FileStorageProviderRegistry([storage], Options.Create(new FileStorageOptions { DefaultProviderKey = "test" })),
            [owner], Substitute.For<IClock>(), Substitute.For<IIdGenerator>(), Options.Create(new LocalFileStorageOptions()),
            new NullTenantFileStorageQuotaPort(), CreateActiveTenants(true), Options.Create(new DatabaseOptions()));

        var result = await store.OpenReadyContentAsync("reporting", resourceId, fileId);
        Assert.AreEqual(authorized, result.IsSuccess);
        if (result.IsSuccess) await result.Value!.Content.DisposeAsync();
        await queries.Received(authorized ? 1 : 0).QuerySingleOrDefaultAsync<TenantResourceFileRecord>(
            TenantResourceFileSql.FindAuthorizedLegacyReference, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        state.HasTransaction.Returns(true);
        queries.ClearReceivedCalls();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            store.OpenReadyContentAsync("reporting", resourceId, fileId));
        Assert.AreEqual(0, queries.ReceivedCalls().Count(), "事务内必须在查询和外部读取前拒绝。");
    }

    /// <summary>真实流越界或已有业务事务时，不允许写元数据或接触对象存储。</summary>
    /// <param name="activeTransaction">是否模拟调用模块已持有事务。</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Unsafe_upload_stops_before_side_effects(bool activeTransaction)
    {
        var tenant = new CurrentTenantAccessor();
        tenant.SetTenant(new TenantContext(Guid.NewGuid(), "a", "A"));
        var commands = Substitute.For<ICommandExecutor>();
        var state = Substitute.For<IDataTransactionState>();
        state.HasTransaction.Returns(activeTransaction);
        var storage = Substitute.For<IFileStorageProvider>();
        storage.ProviderKey.Returns("test");
        var store = Create(Substitute.For<IQueryExecutor>(), commands, tenant, state, storage, Substitute.For<IIdGenerator>());
        using var input = new MemoryStream(new byte[17]);
        var action = () => store.UploadAsync("reporting", Guid.NewGuid(), Guid.NewGuid(), "test.xlsx", "application/test", input, 1);
        if (activeTransaction)
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(action);
        }
        else
        {
            var result = await action();
            Assert.AreEqual(FilesErrorCodes.FileTooLarge, result.Error!.Code);
        }
        Assert.AreEqual(0, commands.ReceivedCalls().Count());
        await storage.DidNotReceive().SaveAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Upload_confirms_storage_quota_before_ready()
    {
        var tenant = new CurrentTenantAccessor();
        var tenantA = new TenantContext(Guid.NewGuid(), "a", "A");
        tenant.SetTenant(tenantA);
        var queries = Substitute.For<IQueryExecutor>();
        var commands = Substitute.For<ICommandExecutor>();
        var storage = Substitute.For<IFileStorageProvider>();
        storage.ProviderKey.Returns("test");
        var quota = Substitute.For<ITenantFileStorageQuotaPort>();
        quota.TryReserveAsync(tenantA.Id, Arg.Any<string>(), 3, Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));
        quota.ConfirmAsync(tenantA.Id, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (call.ArgAt<SqlStatement>(0) == TenantResourceFileSql.MarkReady)
                    Assert.IsTrue(quota.ReceivedCalls().Any(item => item.GetMethodInfo().Name == "ConfirmAsync"));
                return 1;
            });
        storage.SaveAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var fileId = Guid.NewGuid();
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(fileId);
        var store = Create(queries, commands, tenant, Substitute.For<IDataTransactionState>(), storage, ids, quota);
        using var input = new MemoryStream(new byte[] { 1, 2, 3 });
        var upload = await store.UploadAsync(
            "reporting",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "report.xlsx",
            "application/test",
            input,
            3);
        Assert.IsTrue(upload.IsSuccess);
        await quota.Received(1).TryReserveAsync(
            tenantA.Id,
            fileId.ToString("N"),
            3,
            Arg.Any<CancellationToken>());
        await quota.Received(1).ConfirmAsync(
            tenantA.Id,
            fileId.ToString("N"),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Upload_maps_tenancy_quota_failure_to_storage_quota_exceeded()
    {
        var tenant = new CurrentTenantAccessor();
        var tenantA = new TenantContext(Guid.NewGuid(), "a", "A");
        tenant.SetTenant(tenantA);
        var quota = Substitute.For<ITenantFileStorageQuotaPort>();
        quota.TryReserveAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Failure(new Error(
                "tenancy.quota.exceeded",
                "Quota exceeded.",
                ErrorType.Conflict)));
        var storage = Substitute.For<IFileStorageProvider>();
        storage.ProviderKey.Returns("test");
        var store = Create(
            Substitute.For<IQueryExecutor>(),
            Substitute.For<ICommandExecutor>(),
            tenant,
            Substitute.For<IDataTransactionState>(),
            storage,
            Substitute.For<IIdGenerator>(),
            quota);
        using var input = new MemoryStream(new byte[] { 1 });
        var upload = await store.UploadAsync(
            "reporting",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "report.xlsx",
            "application/test",
            input,
            1);
        Assert.IsFalse(upload.IsSuccess);
        Assert.AreEqual(FilesErrorCodes.StorageQuotaExceeded, upload.Error!.Code);
        await storage.DidNotReceive().SaveAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Upload_rejects_inactive_tenant()
    {
        var tenant = new CurrentTenantAccessor();
        tenant.SetTenant(new TenantContext(Guid.NewGuid(), "a", "A"));
        var storage = Substitute.For<IFileStorageProvider>();
        storage.ProviderKey.Returns("test");
        var store = Create(
            Substitute.For<IQueryExecutor>(),
            Substitute.For<ICommandExecutor>(),
            tenant,
            Substitute.For<IDataTransactionState>(),
            storage,
            Substitute.For<IIdGenerator>(),
            tenantActive: false);
        using var input = new MemoryStream(new byte[] { 1 });
        var upload = await store.UploadAsync(
            "reporting",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "report.xlsx",
            "application/test",
            input,
            1);
        Assert.IsFalse(upload.IsSuccess);
        Assert.AreEqual(FilesErrorCodes.TenantInactive, upload.Error!.Code);
    }

    /// <summary>组装只使用受控内存替身的生产文件服务。</summary>
    /// <param name="queries">查询替身。</param>
    /// <param name="commands">写入替身。</param>
    /// <param name="tenant">真实租户上下文。</param>
    /// <param name="state">事务状态。</param>
    /// <param name="storage">对象存储替身。</param>
    /// <param name="ids">标识生成器。</param>
    /// <summary>拒绝或未知的配额确认不能使对象成为可读取的 ready 文件。</summary>
    [TestMethod]
    [DataRow("false")]
    [DataRow("failure")]
    [DataRow("unknown")]
    public async Task Unconfirmed_quota_keeps_upload_unreadable(string outcome)
    {
        var tenant = new CurrentTenantAccessor();
        tenant.SetTenant(new TenantContext(Guid.NewGuid(), "a", "A"));
        var commands = Substitute.For<ICommandExecutor>();
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var storage = Substitute.For<IFileStorageProvider>();
        storage.ProviderKey.Returns("test");
        var quota = Substitute.For<ITenantFileStorageQuotaPort>();
        quota.TryReserveAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));
        quota.ConfirmAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => outcome switch
        {
            "false" => Task.FromResult(Result<bool>.Success(false)),
            "failure" => Task.FromResult(Result<bool>.Failure(new Error("tenancy.quota.exceeded", "Denied", ErrorType.Conflict))),
            _ => Task.FromException<Result<bool>>(new IOException("Confirmation response lost")),
        });
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(Guid.NewGuid());
        var store = Create(Substitute.For<IQueryExecutor>(), commands, tenant, Substitute.For<IDataTransactionState>(), storage, ids, quota);
        using var content = new MemoryStream([1, 2, 3]);
        if (outcome == "unknown") await Assert.ThrowsExactlyAsync<IOException>(() => store.UploadAsync("ai", Guid.NewGuid(), Guid.NewGuid(), "source.txt", "text/plain", content, 3));
        else Assert.IsFalse((await store.UploadAsync("ai", Guid.NewGuid(), Guid.NewGuid(), "source.txt", "text/plain", content, 3)).IsSuccess);
        await commands.DidNotReceive().ExecuteAsync(TenantResourceFileSql.MarkReady, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        if (outcome != "false")
            await quota.DidNotReceive().ReleaseAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>配额端口明确返回不足时，不登记上传意图，也不触碰对象存储。</summary>
    [TestMethod]
    public async Task Reservation_false_stops_before_upload_side_effects()
    {
        var tenant = new CurrentTenantAccessor();
        tenant.SetTenant(new TenantContext(Guid.NewGuid(), "a", "A"));
        var commands = Substitute.For<ICommandExecutor>();
        var storage = Substitute.For<IFileStorageProvider>();
        storage.ProviderKey.Returns("test");
        var quota = Substitute.For<ITenantFileStorageQuotaPort>();
        quota.TryReserveAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(false));
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(Guid.NewGuid());
        var store = Create(Substitute.For<IQueryExecutor>(), commands, tenant, Substitute.For<IDataTransactionState>(), storage, ids, quota);
        using var stream = new MemoryStream([1]);
        Assert.IsFalse((await store.UploadAsync("ai", Guid.NewGuid(), Guid.NewGuid(), "source.txt", "text/plain", stream, 1)).IsSuccess);
        Assert.AreEqual(0, commands.ReceivedCalls().Count());
        await storage.DidNotReceive().SaveAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>());
    }

    /// <summary>pending 清理须先撤读，失败保留墓碑，重新执行后才移除元数据。</summary>
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, false)]
    [DataRow(DatabaseProvider.SqlServer, true)]
    [DataRow(DatabaseProvider.MySql, false)]
    [DataRow(DatabaseProvider.MySql, true)]
    public async Task Bounded_owned_cleanup_handles_unreadable_intents_and_delete_retries(DatabaseProvider provider, bool failFirst)
    {
        var tenant = new CurrentTenantAccessor();
        tenant.SetTenant(new TenantContext(Guid.NewGuid(), "a", "A"));
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var storage = Substitute.For<IFileStorageProvider>();
        storage.ProviderKey.Returns("test");
        var id = Guid.NewGuid();
        var resource = Guid.NewGuid();
        var released = false;
        var purged = false;
        query.QueryAsync<TenantResourceFileReadyRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns([new(id, "source.txt", DateTimeOffset.UtcNow)]);
        query.QuerySingleOrDefaultAsync<TenantResourceFileRecord>(TenantResourceFileSql.FindOwned, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => new TenantResourceFileRecord(id, "source.txt", "text/plain", 1, "hash", "test", "opaque", released ? "released" : "pending"));
        query.QuerySingleOrDefaultAsync<long>(TenantResourceFileSql.CountOwned, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => purged ? 0L : 1L);
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            if (call.ArgAt<SqlStatement>(0) == TenantResourceFileSql.Release) released = true;
            if (call.ArgAt<SqlStatement>(0) == TenantResourceFileSql.PurgeReleased) purged = true;
            return 1;
        });
        var failed = false;
        storage.DeleteAsync("opaque", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            Assert.IsTrue(released);
            if (failFirst && !failed) { failed = true; return Task.FromException(new IOException("Delete interrupted")); }
            return Task.CompletedTask;
        });
        var store = Create(query, command, tenant, Substitute.For<IDataTransactionState>(), storage, Substitute.For<IIdGenerator>(), provider: provider);
        if (failFirst)
        {
            await Assert.ThrowsExactlyAsync<IOException>(() => store.ReleaseOwnedAsync("ai", resource));
            Assert.IsFalse(purged);
        }
        Assert.IsTrue(await store.ReleaseOwnedAsync("ai", resource));
        Assert.IsTrue(purged);
    }

    /// <summary>释放失败 Result 保留墓碑及重试凭据，下一次成功才能完成清理。</summary>
    [TestMethod]
    public async Task Owned_cleanup_retains_intent_when_quota_release_fails()
    {
        var tenant = new CurrentTenantAccessor();
        tenant.SetTenant(new TenantContext(Guid.NewGuid(), "a", "A"));
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var storage = Substitute.For<IFileStorageProvider>();
        storage.ProviderKey.Returns("test");
        var quota = Substitute.For<ITenantFileStorageQuotaPort>();
        quota.ReleaseAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
            Result<bool>.Failure(new Error("quota.transient", "Retry", ErrorType.Conflict)), Result<bool>.Success(true));
        var id = Guid.NewGuid();
        var purged = false;
        query.QueryAsync<TenantResourceFileReadyRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns([new(id, "source.txt", DateTimeOffset.UtcNow)]);
        query.QuerySingleOrDefaultAsync<TenantResourceFileRecord>(TenantResourceFileSql.FindOwned, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new TenantResourceFileRecord(id, "source.txt", "text/plain", 1, "hash", "test", "opaque", "released"));
        query.QuerySingleOrDefaultAsync<long>(TenantResourceFileSql.CountOwned, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => purged ? 0L : 1L);
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call => { if (call.ArgAt<SqlStatement>(0) == TenantResourceFileSql.PurgeReleased) purged = true; return 1; });
        var store = Create(query, command, tenant, Substitute.For<IDataTransactionState>(), storage, Substitute.For<IIdGenerator>(), quota);
        var resource = Guid.NewGuid();
        Assert.IsFalse(await store.ReleaseOwnedAsync("ai", resource));
        Assert.IsFalse(purged);
        Assert.IsTrue(await store.ReleaseOwnedAsync("ai", resource));
    }

    /// <summary>清理在旧上传发布前完成时，旧执行者也必须删除其刚保存的孤立对象。</summary>
    [TestMethod]
    [DataRow("false")]
    [DataRow("true")]
    [DataRow("failure")]
    [DataRow("throw")]
    [DataRow("cancel")]
    public async Task Late_upload_deletes_object_after_intent_was_purged(string outcome)
    {
        var tenant = new CurrentTenantAccessor();
        tenant.SetTenant(new TenantContext(Guid.NewGuid(), "a", "A"));
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var storage = Substitute.For<IFileStorageProvider>();
        storage.ProviderKey.Returns("test");
        var quota = Substitute.For<ITenantFileStorageQuotaPort>();
        quota.TryReserveAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));
        quota.ConfirmAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => outcome switch
        {
            "failure" => Task.FromResult(Result<bool>.Failure(new Error("quota.transient", "Retry", ErrorType.Conflict))),
            "throw" => Task.FromException<Result<bool>>(new IOException("Response lost")),
            "cancel" => Task.FromException<Result<bool>>(new OperationCanceledException()),
            _ => Task.FromResult(Result<bool>.Success(outcome == "true")),
        });
        quota.ReleaseAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));
        var id = Guid.NewGuid();
        var resource = Guid.NewGuid();
        var purged = false;
        var exists = false;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        query.QueryAsync<TenantResourceFileReadyRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns([new(id, "source.txt", DateTimeOffset.UtcNow)]);
        query.QuerySingleOrDefaultAsync<TenantResourceFileRecord>(TenantResourceFileSql.FindOwned, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => purged ? null : new TenantResourceFileRecord(id, "source.txt", "text/plain", 1, "hash", "test", $"tenant-resources/{tenant.Id:N}/{id:N}", "pending"));
        query.QuerySingleOrDefaultAsync<long>(TenantResourceFileSql.CountOwned, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => purged ? 0L : 1L);
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call => { if (call.ArgAt<SqlStatement>(0) == TenantResourceFileSql.PurgeReleased) purged = true; return purged ? 0 : 1; });
        storage.SaveAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns(async _ => { entered.TrySetResult(); await resume.Task; exists = true; });
        storage.DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => { exists = false; return Task.CompletedTask; });
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(id);
        var store = Create(query, command, tenant, Substitute.For<IDataTransactionState>(), storage, ids, quota);
        using var stream = new MemoryStream([1]);
        var upload = store.UploadAsync("ai", resource, Guid.NewGuid(), "source.txt", "text/plain", stream, 1);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try { Assert.IsTrue(await store.ReleaseOwnedAsync("ai", resource)); }
        finally { resume.TrySetResult(); }
        if (outcome == "throw") await Assert.ThrowsExactlyAsync<IOException>(() => upload);
        else if (outcome == "cancel") await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => upload);
        else Assert.IsFalse((await upload).IsSuccess);
        Assert.IsFalse(exists, "晚到上传不能留下 Worker 已无元数据可追踪的对象。");
    }

    private static TenantResourceFileStore Create(IQueryExecutor queries, ICommandExecutor commands,
        CurrentTenantAccessor tenant, IDataTransactionState state, IFileStorageProvider storage, IIdGenerator ids,
        ITenantFileStorageQuotaPort? quotaPort = null, bool tenantActive = true, DatabaseProvider provider = DatabaseProvider.SqlServer) =>
        new(queries, commands, tenant, state,
            new FileStorageProviderRegistry([storage], Options.Create(new FileStorageOptions { DefaultProviderKey = "test" })),
            [], Substitute.For<IClock>(), ids, Options.Create(new LocalFileStorageOptions { MaxUploadBytes = 16 }),
            quotaPort ?? new NullTenantFileStorageQuotaPort(), CreateActiveTenants(tenantActive), Options.Create(new DatabaseOptions { Provider = provider }));

    private static IIdentityActiveTenantDirectory CreateActiveTenants(bool active)
    {
        var directory = Substitute.For<IIdentityActiveTenantDirectory>();
        directory.IsActiveTenantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(active);
        return directory;
    }
}
