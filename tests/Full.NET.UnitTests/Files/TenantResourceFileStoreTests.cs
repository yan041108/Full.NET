using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.TenantResourceFiles;
using Full.NET.Modules.Files.Persistence;
using Full.NET.Modules.Files.Storage;
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
            [owner], Substitute.For<IClock>(), Substitute.For<IIdGenerator>(), Options.Create(new LocalFileStorageOptions()));

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

    /// <summary>组装只使用受控内存替身的生产文件服务。</summary>
    /// <param name="queries">查询替身。</param>
    /// <param name="commands">写入替身。</param>
    /// <param name="tenant">真实租户上下文。</param>
    /// <param name="state">事务状态。</param>
    /// <param name="storage">对象存储替身。</param>
    /// <param name="ids">标识生成器。</param>
    private static TenantResourceFileStore Create(IQueryExecutor queries, ICommandExecutor commands,
        CurrentTenantAccessor tenant, IDataTransactionState state, IFileStorageProvider storage, IIdGenerator ids) =>
        new(queries, commands, tenant, state,
            new FileStorageProviderRegistry([storage], Options.Create(new FileStorageOptions { DefaultProviderKey = "test" })),
            [], Substitute.For<IClock>(), ids, Options.Create(new LocalFileStorageOptions { MaxUploadBytes = 16 }));
}
