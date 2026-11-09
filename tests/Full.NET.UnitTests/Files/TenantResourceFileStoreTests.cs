using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.TenantResourceFiles;
using Full.NET.Modules.Files.Persistence;
using Full.NET.Modules.Files.Reconciliation;
using Full.NET.Modules.Files.Storage;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Files;

/// <summary>租户资源文件只能由同一租户、模块和资源访问，外发前必须存在独立提交的意图。</summary>
[TestClass]
public sealed class TenantResourceFileStoreTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task List_ready_rejects_inactive_tenant_before_or_after_query(bool afterQuery)
    {
        var tenant = new CurrentTenantAccessor(); var tenantId = Guid.NewGuid(); tenant.SetTenant(new(tenantId, "a", "A"));
        var queries = Substitute.For<IQueryExecutor>();
        queries.QueryAsync<TenantResourceFileReadyRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns([new(Guid.NewGuid(), "private.txt", DateTimeOffset.UtcNow)]);
        var active = Substitute.For<IIdentityActiveTenantDirectory>();
        active.IsActiveTenantAsync(tenantId, Arg.Any<CancellationToken>()).Returns(afterQuery, false);
        var storage = Substitute.For<IFileStorageProvider>(); storage.ProviderKey.Returns("test");
        var store = Create(queries, Substitute.For<ICommandExecutor>(), tenant, Substitute.For<IDataTransactionState>(),
            storage, Substitute.For<IIdGenerator>(), activeTenants: active);
        await Assert.ThrowsExactlyAsync<TenantResourceFileUnavailableException>(() => store.ListReadyAsync("reporting", Guid.NewGuid()));
        await queries.Received(afterQuery ? 1 : 0).QueryAsync<TenantResourceFileReadyRecord>(
            TenantResourceFileSql.ListReady, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("inactive")]
    [DataRow("tenant")]
    [DataRow("host")]
    [DataRow("cleared")]
    [DataRow("cancel")]
    public async Task List_ready_discards_rows_when_authority_changes_during_query(string mode)
    {
        var tenant = new CurrentTenantAccessor(); var tenantId = Guid.NewGuid(); tenant.SetTenant(new(tenantId, "a", "A"));
        var queries = Substitute.For<IQueryExecutor>();
        var pending = new TaskCompletionSource<IReadOnlyList<TenantResourceFileReadyRecord>>(TaskCreationOptions.RunContinuationsAsynchronously);
        queries.QueryAsync<TenantResourceFileReadyRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(pending.Task);
        var active = CreateActiveTenants(true);
        var storage = Substitute.For<IFileStorageProvider>(); storage.ProviderKey.Returns("test");
        var store = Create(queries, Substitute.For<ICommandExecutor>(), tenant, Substitute.For<IDataTransactionState>(),
            storage, Substitute.For<IIdGenerator>(), activeTenants: active);
        using var cancellation = new CancellationTokenSource();
        var operation = store.ListReadyAsync("reporting", Guid.NewGuid(), cancellation.Token);
        if (mode == "inactive") active.IsActiveTenantAsync(tenantId, Arg.Any<CancellationToken>()).Returns(false);
        else if (mode == "tenant") tenant.SetTenant(new(Guid.NewGuid(), "b", "B"));
        else if (mode == "host") tenant.SetHost();
        else if (mode == "cleared") tenant.Clear();
        else cancellation.Cancel();
        pending.SetResult([new(Guid.NewGuid(), "private.txt", DateTimeOffset.UtcNow)]);
        if (mode == "cancel") await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => operation);
        else await Assert.ThrowsExactlyAsync<TenantResourceFileUnavailableException>(() => operation);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task List_ready_discards_active_directory_result_after_context_change(bool afterQuery)
    {
        var tenant = new CurrentTenantAccessor(); var tenantId = Guid.NewGuid(); tenant.SetTenant(new(tenantId, "a", "A"));
        var queries = Substitute.For<IQueryExecutor>();
        queries.QueryAsync<TenantResourceFileReadyRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns([new(Guid.NewGuid(), "private.txt", DateTimeOffset.UtcNow)]);
        var active = CreateActiveTenants(true); var checks = 0;
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        active.IsActiveTenantAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(_ => ++checks == (afterQuery ? 2 : 1) ? pending.Task : Task.FromResult(true));
        var storage = Substitute.For<IFileStorageProvider>(); storage.ProviderKey.Returns("test");
        var store = Create(queries, Substitute.For<ICommandExecutor>(), tenant, Substitute.For<IDataTransactionState>(),
            storage, Substitute.For<IIdGenerator>(), activeTenants: active);
        var operation = store.ListReadyAsync("reporting", Guid.NewGuid());
        tenant.SetHost(); pending.SetResult(true);
        await Assert.ThrowsExactlyAsync<TenantResourceFileUnavailableException>(() => operation);
        await queries.Received(afterQuery ? 1 : 0).QueryAsync<TenantResourceFileReadyRecord>(
            TenantResourceFileSql.ListReady, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task List_ready_returns_original_files_for_active_tenant()
    {
        var tenant = new CurrentTenantAccessor(); tenant.SetTenant(new(Guid.NewGuid(), "a", "A"));
        var queries = Substitute.For<IQueryExecutor>(); var file = Guid.NewGuid(); var resource = Guid.NewGuid();
        queries.QueryAsync<TenantResourceFileReadyRecord>(TenantResourceFileSql.ListReady,
            Arg.Is<Dictionary<string, object?>>(p => p != null && (string)p["OwnerModuleKey"]! == "reporting" && (Guid)p["ResourceId"]! == resource),
            Arg.Any<CancellationToken>()).Returns([new(file, "report.xlsx", DateTimeOffset.UtcNow)]);
        var storage = Substitute.For<IFileStorageProvider>(); storage.ProviderKey.Returns("test");
        var store = Create(queries, Substitute.For<ICommandExecutor>(), tenant, Substitute.For<IDataTransactionState>(),
            storage, Substitute.For<IIdGenerator>());
        var result = await store.ListReadyAsync("reporting", resource);
        Assert.AreEqual(1, result.Count); Assert.AreEqual(file, result[0].FileId); Assert.AreEqual("report.xlsx", result[0].OriginalFileName);
    }

    [TestMethod]
    public async Task List_ready_rejects_foreign_transaction_before_activity_check()
    {
        var tenant = new CurrentTenantAccessor(); tenant.SetTenant(new(Guid.NewGuid(), "a", "A"));
        var queries = Substitute.For<IQueryExecutor>(); var active = CreateActiveTenants(true);
        var state = Substitute.For<IDataTransactionState>(); state.HasTransaction.Returns(true);
        var storage = Substitute.For<IFileStorageProvider>(); storage.ProviderKey.Returns("test");
        var store = Create(queries, Substitute.For<ICommandExecutor>(), tenant, state, storage,
            Substitute.For<IIdGenerator>(), activeTenants: active);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => store.ListReadyAsync("reporting", Guid.NewGuid()));
        Assert.AreEqual(0, queries.ReceivedCalls().Count());
        Assert.AreEqual(0, active.ReceivedCalls().Count());
    }

    /// <summary>非活动企业在读取元数据或打开外部对象前即拒绝。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Open_revalidates_tenant_before_metadata_and_external_object(bool revokedAfterMetadata)
    {
        var tenant = new CurrentTenantAccessor(); var tenantId = Guid.NewGuid(); tenant.SetTenant(new(tenantId, "a", "A"));
        var queries = Substitute.For<IQueryExecutor>(); var fileId = Guid.NewGuid();
        queries.QuerySingleOrDefaultAsync<TenantResourceFileRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new TenantResourceFileRecord(fileId, "file.txt", "text/plain", 1, "hash", "test", "object-key", "ready"));
        var directory = Substitute.For<IIdentityActiveTenantDirectory>();
        directory.IsActiveTenantAsync(tenantId, Arg.Any<CancellationToken>()).Returns(revokedAfterMetadata, false);
        var storage = Substitute.For<IFileStorageProvider>(); storage.ProviderKey.Returns("test");
        var store = Create(queries, Substitute.For<ICommandExecutor>(), tenant, Substitute.For<IDataTransactionState>(), storage,
            Substitute.For<IIdGenerator>(), activeTenants: directory);
        var result = await store.OpenReadyContentAsync("reporting", Guid.NewGuid(), fileId);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(FilesErrorCodes.TenantInactive, result.Error!.Code);
        await queries.Received(revokedAfterMetadata ? 1 : 0).QuerySingleOrDefaultAsync<TenantResourceFileRecord>(
            Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await storage.DidNotReceive().OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>外部对象已经打开时，晚到的撤销、上下文变化、取消或目录异常不能泄露流。</summary>
    [TestMethod]
    [DataRow("inactive")]
    [DataRow("scope")]
    [DataRow("host")]
    [DataRow("cleared")]
    [DataRow("exception")]
    [DataRow("cancel")]
    public async Task Open_releases_stream_when_original_tenant_cannot_receive_content(string mode)
    {
        var tenant = new CurrentTenantAccessor(); var tenantId = Guid.NewGuid(); tenant.SetTenant(new(tenantId, "a", "A"));
        var queries = Substitute.For<IQueryExecutor>(); var fileId = Guid.NewGuid();
        queries.QuerySingleOrDefaultAsync<TenantResourceFileRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new TenantResourceFileRecord(fileId, "file.txt", "text/plain", 1, "hash", "test", "object-key", "ready"));
        var directory = Substitute.For<IIdentityActiveTenantDirectory>(); var checks = 0;
        using var cancellation = new CancellationTokenSource();
        directory.IsActiveTenantAsync(tenantId, Arg.Any<CancellationToken>()).Returns(_ => {
            checks++;
            if (checks == 3 && mode == "exception") throw new InvalidOperationException("directory unavailable");
            return checks < 3 || mode != "inactive";
        });
        var content = new MemoryStream(new byte[] { 1 });
        var storage = Substitute.For<IFileStorageProvider>(); storage.ProviderKey.Returns("test");
        storage.OpenReadAsync("object-key", Arg.Any<CancellationToken>()).Returns(_ => {
            if (mode == "scope") tenant.SetTenant(new(Guid.NewGuid(), "b", "B"));
            if (mode == "host") tenant.SetHost();
            if (mode == "cleared") tenant.Clear();
            if (mode == "cancel") cancellation.Cancel();
            return content;
        });
        var store = Create(queries, Substitute.For<ICommandExecutor>(), tenant, Substitute.For<IDataTransactionState>(), storage,
            Substitute.For<IIdGenerator>(), activeTenants: directory);
        if (mode == "exception") await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => store.OpenReadyContentAsync("reporting", Guid.NewGuid(), fileId, cancellation.Token));
        else if (mode == "cancel") await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => store.OpenReadyContentAsync("reporting", Guid.NewGuid(), fileId, cancellation.Token));
        else { var result = await store.OpenReadyContentAsync("reporting", Guid.NewGuid(), fileId, cancellation.Token); Assert.IsFalse(result.IsSuccess); Assert.AreEqual(FilesErrorCodes.TenantInactive, result.Error!.Code); }
        Assert.IsFalse(content.CanRead, "未交付的流必须释放。");
        await storage.Received(1).OpenReadAsync("object-key", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Upload_revalidates_tenant_after_buffering_before_quota_or_object_writes(bool changedScope)
    {
        var tenant = new CurrentTenantAccessor(); var tenantId = Guid.NewGuid();
        tenant.SetTenant(new(tenantId, "a", "A"));
        var active = Substitute.For<IIdentityActiveTenantDirectory>();
        active.IsActiveTenantAsync(tenantId, Arg.Any<CancellationToken>()).Returns(true, false);
        var storage = Substitute.For<IFileStorageProvider>(); storage.ProviderKey.Returns("test");
        var quota = Substitute.For<ITenantFileStorageQuotaPort>(); var commands = Substitute.For<ICommandExecutor>();
        var store = Create(Substitute.For<IQueryExecutor>(), commands, tenant, Substitute.For<IDataTransactionState>(), storage, Substitute.For<IIdGenerator>(), quotaPort: quota, activeTenants: active);
        using var content = new CallbackReadStream(() => { if (changedScope) tenant.SetTenant(new(Guid.NewGuid(), "b", "B")); });
        var result = await store.UploadAsync("reporting", Guid.NewGuid(), Guid.NewGuid(), "result.xlsx", "application/test", content, 1);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(FilesErrorCodes.TenantInactive, result.Error!.Code);
        await quota.DidNotReceive().TryReserveAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
        await commands.DidNotReceive().ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await storage.DidNotReceive().SaveAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Ready_metadata_with_missing_object_returns_not_found(bool directory)
    {
        var tenant = new CurrentTenantAccessor(); tenant.SetTenant(new(Guid.NewGuid(), "a", "A"));
        var fileId = Guid.NewGuid(); var queries = Substitute.For<IQueryExecutor>();
        queries.QuerySingleOrDefaultAsync<TenantResourceFileRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new TenantResourceFileRecord(fileId, "receipt.xlsx", "application/test", 1, "hash", "test", "private-key", "ready"));
        var storage = Substitute.For<IFileStorageProvider>(); storage.ProviderKey.Returns("test");
        Exception error = directory ? new DirectoryNotFoundException("private") : new FileNotFoundException("private", "private-key");
        storage.OpenReadAsync("private-key", Arg.Any<CancellationToken>()).Returns(Task.FromException<Stream>(error));
        var store = Create(queries, Substitute.For<ICommandExecutor>(), tenant, Substitute.For<IDataTransactionState>(), storage, Substitute.For<IIdGenerator>());
        var result = await store.OpenReadyContentAsync("import_export", Guid.NewGuid(), fileId);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(FilesErrorCodes.FileNotFound, result.Error!.Code);
        Assert.AreEqual(ErrorType.NotFound, result.Error.Type); Assert.IsFalse(result.Error.Message.Contains("private", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Upload_owner_intent_is_committed_before_object_access_or_blocks_upload(bool ownerFails)
    {
        var tenant = new CurrentTenantAccessor(); tenant.SetTenant(new(Guid.NewGuid(), "a", "A"));
        var ids = Substitute.For<IIdGenerator>(); var fileId = Guid.NewGuid(); ids.NewId().Returns(fileId);
        var resourceId = Guid.NewGuid(); var actor = Guid.NewGuid(); var tracked = false;
        var owner = Substitute.For<ITenantResourceFileUploadOwner>(); owner.OwnerModuleKey.Returns("reporting");
        owner.BeginUploadAsync(Arg.Any<TenantResourceFileUploadIntent>(), Arg.Any<CancellationToken>()).Returns(call => {
            var intent = call.ArgAt<TenantResourceFileUploadIntent>(0);
            Assert.AreEqual(resourceId, intent.ResourceId); Assert.AreEqual(fileId, intent.FileId); Assert.AreEqual(actor, intent.ActorUserId);
            Assert.AreEqual(1L, intent.SizeBytes); tracked = true;
            return ownerFails ? Task.FromException(new InvalidOperationException("probe")) : Task.CompletedTask;
        });
        var commands = Substitute.For<ICommandExecutor>(); commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var storage = Substitute.For<IFileStorageProvider>(); storage.ProviderKey.Returns("test");
        storage.SaveAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns(_ => { Assert.IsTrue(tracked); return Task.CompletedTask; });
        var quota = Substitute.For<ITenantFileStorageQuotaPort>();
        quota.TryReserveAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));
        quota.ConfirmAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));
        var store = Create(Substitute.For<IQueryExecutor>(), commands, tenant, Substitute.For<IDataTransactionState>(), storage, ids, quota, owners: [owner]);
        using var stream = new MemoryStream([1]);
        if (ownerFails)
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => store.UploadAsync("reporting", resourceId, actor, "probe.txt", "application/test", stream, 1));
            await storage.DidNotReceive().SaveAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>());
            await quota.Received(1).ReleaseAsync(tenant.Id!.Value, fileId.ToString("N"), Arg.Any<CancellationToken>());
        }
        else Assert.IsTrue((await store.UploadAsync("reporting", resourceId, actor, "probe.txt", "application/test", stream, 1)).IsSuccess);
    }
    [TestMethod]
    public async Task Lost_pending_metadata_after_save_removes_object_instead_of_leaking_it()
    {
        var tenant = new CurrentTenantAccessor(); var tenantId = Guid.NewGuid();
        tenant.SetTenant(new(tenantId, "a", "A")); var fileId = Guid.NewGuid();
        var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(fileId);
        var commands = Substitute.For<ICommandExecutor>();
        commands.ExecuteAsync(TenantResourceFileSql.Insert, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        commands.ExecuteAsync(TenantResourceFileSql.InsertReleasedIntent, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var storage = Substitute.For<IFileStorageProvider>(); storage.ProviderKey.Returns("test");
        var store = Create(Substitute.For<IQueryExecutor>(), commands, tenant, Substitute.For<IDataTransactionState>(), storage, ids);
        using var stream = new MemoryStream([1]);
        Assert.IsFalse((await store.UploadAsync("reporting", Guid.NewGuid(), Guid.NewGuid(), "probe.txt", "application/test", stream, 1)).IsSuccess);
        await storage.Received(1).DeleteAsync($"tenant-resources/{tenantId:N}/{fileId:N}", Arg.Any<CancellationToken>());
    }
    /// <summary>迟到对象删除失败前必须恢复不可读取的墓碑，供 Worker 后续回收。</summary>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public async Task Lost_pending_metadata_keeps_cleanup_intent_when_delete_fails(bool cancelled, bool quotaFails)
    {
        var tenant = new CurrentTenantAccessor(); var tenantId = Guid.NewGuid();
        tenant.SetTenant(new(tenantId, "a", "A")); var fileId = Guid.NewGuid();
        var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(fileId);
        var commands = Substitute.For<ICommandExecutor>(); var cleanupTracked = false;
        var resourceId = Guid.NewGuid(); var key = $"tenant-resources/{tenantId:N}/{fileId:N}";
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call => {
            var statement = call.ArgAt<SqlStatement>(0);
            if (statement.Name == "files.tenant_resource_file.insert_released_intent")
            {
                SqlScopeGuard.Validate(statement, tenant);
                StringAssert.Contains(statement.Text, "'released'");
                var parameters = (Dictionary<string, object?>)call.ArgAt<object>(1);
                Assert.AreEqual(fileId, parameters["Id"]);
                Assert.AreEqual(key, parameters["StorageKey"]);
                Assert.AreEqual(resourceId, parameters["ResourceId"]);
                cleanupTracked = true;
            }
            if (statement == TenantResourceFileSql.PurgeReleased) cleanupTracked = false;
            return statement == TenantResourceFileSql.MarkReady ? 0 : 1;
        });
        var storage = Substitute.For<IFileStorageProvider>(); storage.ProviderKey.Returns("test");
        using var cancellation = new CancellationTokenSource(); var attempts = 0;
        storage.DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => {
            Assert.IsTrue(cleanupTracked, "物理删除失败或取消之前必须有可重试墓碑。");
            if (++attempts > 1 || quotaFails) return Task.CompletedTask;
            if (cancelled) { cancellation.Cancel(); return Task.FromException(new OperationCanceledException(cancellation.Token)); }
            return Task.FromException(new IOException("Injected delete failure."));
        });
        var queries = Substitute.For<IQueryExecutor>();
        queries.QuerySingleOrDefaultAsync<TenantResourceFileRecord>(TenantResourceFileSql.FindOwned, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(_ => cleanupTracked ? new(fileId, "probe.txt", "application/test", 1, "hash", "test", key, "released") : null);
        var quota = Substitute.For<ITenantFileStorageQuotaPort>();
        quota.TryReserveAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));
        quota.ConfirmAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));
        quota.ReleaseAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => {
            if (quotaFails) { Assert.IsTrue(cleanupTracked, "配额释放异常之前也必须有可发现墓碑。"); return Task.FromException<Result<bool>>(new IOException("Injected quota release failure.")); }
            return Task.FromResult(Result<bool>.Success(true));
        });
        var store = Create(queries, commands, tenant, Substitute.For<IDataTransactionState>(), storage, ids, quota);
        using var stream = new MemoryStream([1]);
        Task<Result<TenantResourceFileReference>> Upload() => store.UploadAsync("reporting", resourceId, Guid.NewGuid(), "probe.txt", "application/test", stream, 1, cancellation.Token);
        if (cancelled) await Assert.ThrowsExactlyAsync<OperationCanceledException>(Upload);
        else if (quotaFails) await Assert.ThrowsExactlyAsync<IOException>(Upload);
        else
        {
            var conflict = await Upload();
            Assert.IsFalse(conflict.IsSuccess);
            Assert.AreEqual(FilesErrorCodes.RevisionConflict, conflict.Error?.Code);
        }
        Assert.IsTrue(cleanupTracked);
        await commands.DidNotReceive().ExecuteAsync(TenantResourceFileSql.PurgeReleased, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        // 使用同一精确墓碑运行真实 Worker；第二次删除成功后才能清除记录。
        queries.QueryAsync<TenantResourceFileReconciliationRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(_ => cleanupTracked ? [new(fileId, tenantId, "reporting", resourceId, "test", key, "released", DateTimeOffset.UtcNow.AddHours(-1))] : []);
        var resolver = Substitute.For<IActiveTenantContextResolver>();
        resolver.ResolveActiveByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(new TenantContext(tenantId, "a", "A"));
        var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var runner = new PendingTenantResourceFileReconciliationRunner(queries, commands,
            new FileStorageProviderRegistry([storage], Options.Create(new FileStorageOptions { DefaultProviderKey = "test" })),
            [], resolver, tenant, Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }), clock, new NullTenantFileStorageQuotaPort());
        var result = await runner.RunOnceAsync(new() { Enabled = true, BatchSize = 100, MaxBatchesPerRun = 1, MinimumAgeSeconds = 30 }, CancellationToken.None);
        Assert.AreEqual(1, result.Released); Assert.AreEqual(2, attempts); Assert.IsFalse(cleanupTracked);
    }
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
        ITenantFileStorageQuotaPort? quotaPort = null, bool tenantActive = true, IEnumerable<ITenantResourceFileOwner>? owners = null,
        IIdentityActiveTenantDirectory? activeTenants = null, DatabaseProvider provider = DatabaseProvider.SqlServer) =>
        new(queries, commands, tenant, state,
            new FileStorageProviderRegistry([storage], Options.Create(new FileStorageOptions { DefaultProviderKey = "test" })),
            owners ?? [], Substitute.For<IClock>(), ids, Options.Create(new LocalFileStorageOptions { MaxUploadBytes = 16 }),
            quotaPort ?? new NullTenantFileStorageQuotaPort(), activeTenants ?? CreateActiveTenants(tenantActive), Options.Create(new DatabaseOptions { Provider = provider }));

    /// <summary>在真实读取边界改变租户状态，不伪造配额、数据库或对象上传结果。</summary>
    private sealed class CallbackReadStream(Action callback) : MemoryStream(new byte[] { 1 })
    {
        private bool called;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!called) { called = true; callback(); }
            return base.ReadAsync(buffer, cancellationToken);
        }
    }

    private static IIdentityActiveTenantDirectory CreateActiveTenants(bool active)
    {
        var directory = Substitute.For<IIdentityActiveTenantDirectory>();
        directory.IsActiveTenantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(active);
        return directory;
    }
}
