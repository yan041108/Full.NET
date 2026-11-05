using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Features.TenantResourceFiles;
using Full.NET.Modules.Files.Persistence;
using Full.NET.Modules.Identity.Contracts;
using NSubstitute;

namespace Full.NET.UnitTests.Files;

/// <summary>描述 Port 的租户与资源守卫不能被消费模块的文件 UUID 绕过。</summary>
[TestClass]
public sealed class TenantResourceFileDescriptorReaderTests
{
    private static IIdentityActiveTenantDirectory Active()
    {
        var directory = Substitute.For<IIdentityActiveTenantDirectory>();
        directory.IsActiveTenantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        return directory;
    }

    /// <summary>已停用企业不能先读取安全元数据再依赖消费模块补检查。</summary>
    [TestMethod]
    public async Task Inactive_tenant_does_not_query_descriptor()
    {
        var tenant = new CurrentTenantAccessor(); tenant.SetTenant(new(Guid.CreateVersion7(), "a", "A"));
        var query = Substitute.For<IQueryExecutor>(); var directory = Active();
        directory.IsActiveTenantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        var reader = new TenantResourceFileDescriptorReader(query, tenant, Substitute.For<IDataTransactionState>(), directory);
        Assert.IsNull(await reader.GetReadyDescriptorAsync("workflow", Guid.CreateVersion7(), Guid.CreateVersion7()));
        Assert.AreEqual(0, query.ReceivedCalls().Count());
    }

    /// <summary>精确查询返回也必须仍属于原企业，不能返回晚到的文件名、摘要和上传人。</summary>
    [TestMethod]
    [DataRow("inactive")]
    [DataRow("tenant")]
    [DataRow("host")]
    [DataRow("cleared")]
    public async Task Descriptor_arriving_after_revocation_or_scope_change_is_discarded(string mode)
    {
        var tenant = new CurrentTenantAccessor(); tenant.SetTenant(new(Guid.CreateVersion7(), "a", "A"));
        var query = Substitute.For<IQueryExecutor>(); var directory = Active();
        var pending = new TaskCompletionSource<TenantResourceFileDescriptorRecord?>(TaskCreationOptions.RunContinuationsAsynchronously);
        query.QuerySingleOrDefaultAsync<TenantResourceFileDescriptorRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(pending.Task);
        var reader = new TenantResourceFileDescriptorReader(query, tenant, Substitute.For<IDataTransactionState>(), directory);
        var file = Guid.CreateVersion7(); var operation = reader.GetReadyDescriptorAsync("workflow", Guid.CreateVersion7(), file);
        if (mode == "inactive") directory.IsActiveTenantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        else if (mode == "tenant") tenant.SetTenant(new(Guid.CreateVersion7(), "b", "B"));
        else if (mode == "host") tenant.SetHost();
        else tenant.Clear();
        pending.SetResult(new(file, "private.txt", "text/plain", 3, "private-hash", Guid.CreateVersion7()));
        Assert.IsNull(await operation);
    }

    /// <summary>活动目录本身也是异步边界，返回 true 不能覆盖原上下文已失效的事实。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Active_directory_result_cannot_restore_changed_context(bool afterQuery)
    {
        var tenant = new CurrentTenantAccessor(); tenant.SetTenant(new(Guid.CreateVersion7(), "a", "A"));
        var query = Substitute.For<IQueryExecutor>(); var directory = Active(); var checks = 0;
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        directory.IsActiveTenantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ => ++checks == (afterQuery ? 2 : 1) ? pending.Task : Task.FromResult(true));
        var file = Guid.CreateVersion7();
        query.QuerySingleOrDefaultAsync<TenantResourceFileDescriptorRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new TenantResourceFileDescriptorRecord(file, "private.txt", "text/plain", 3, "hash", file));
        var reader = new TenantResourceFileDescriptorReader(query, tenant, Substitute.For<IDataTransactionState>(), directory);
        var operation = reader.GetReadyDescriptorAsync("workflow", Guid.CreateVersion7(), file);
        tenant.SetHost(); pending.SetResult(true); Assert.IsNull(await operation);
        Assert.AreEqual(afterQuery ? 1 : 0, query.ReceivedCalls().Count());
    }

    /// <summary>底层忽略取消时也不能正常交付已经返回的描述。</summary>
    [TestMethod]
    public async Task Cancellation_after_query_does_not_deliver_metadata()
    {
        var tenant = new CurrentTenantAccessor(); tenant.SetTenant(new(Guid.CreateVersion7(), "a", "A"));
        var query = Substitute.For<IQueryExecutor>(); var pending = new TaskCompletionSource<TenantResourceFileDescriptorRecord?>(TaskCreationOptions.RunContinuationsAsynchronously);
        query.QuerySingleOrDefaultAsync<TenantResourceFileDescriptorRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(pending.Task);
        var reader = new TenantResourceFileDescriptorReader(query, tenant, Substitute.For<IDataTransactionState>(), Active());
        using var cancellation = new CancellationTokenSource();
        var operation = reader.GetReadyDescriptorAsync("workflow", Guid.CreateVersion7(), Guid.CreateVersion7(), cancellation.Token);
        cancellation.Cancel(); pending.SetResult(new(Guid.CreateVersion7(), "private.txt", "text/plain", 3, "hash", Guid.CreateVersion7()));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => operation);
    }

    [TestMethod]
    public async Task Read_binds_trusted_tenant_module_resource_and_ready_file()
    {
        var tenant = new CurrentTenantAccessor(); var tenantId = Guid.CreateVersion7();
        tenant.SetTenant(new TenantContext(tenantId, "acme", "Acme"));
        var query = Substitute.For<IQueryExecutor>(); var resource = Guid.CreateVersion7(); var file = Guid.CreateVersion7();
        query.QuerySingleOrDefaultAsync<TenantResourceFileDescriptorRecord>(TenantResourceFileSql.FindReadyDescriptor,
            Arg.Is<Dictionary<string, object?>>(parameters => parameters != null && (Guid)parameters["TenantId"]! == tenantId &&
                (Guid)parameters["ResourceId"]! == resource && (Guid)parameters["Id"]! == file && (string)parameters["OwnerModuleKey"]! == "workflow"),
            Arg.Any<CancellationToken>()).Returns(new TenantResourceFileDescriptorRecord(file, "signature.png", "image/png", 8, "hash", resource));
        var reader = new TenantResourceFileDescriptorReader(query, tenant, Substitute.For<IDataTransactionState>(), Active());
        var result = await reader.GetReadyDescriptorAsync("workflow", resource, file);
        Assert.AreEqual(file, result?.FileId);
        Assert.AreEqual(resource, result?.CreatedByUserId);
        Assert.IsNull(await reader.GetReadyDescriptorAsync("workflow", Guid.CreateVersion7(), file));
    }

    [TestMethod]
    public async Task Foreign_transaction_and_host_context_are_rejected_before_query()
    {
        var tenant = new CurrentTenantAccessor(); tenant.SetHost();
        var query = Substitute.For<IQueryExecutor>(); var transaction = Substitute.For<IDataTransactionState>();
        var reader = new TenantResourceFileDescriptorReader(query, tenant, transaction, Active());
        await Assert.ThrowsExactlyAsync<TenantContextMissingException>(() => reader.GetReadyDescriptorAsync("workflow", Guid.CreateVersion7(), Guid.CreateVersion7()));
        transaction.HasTransaction.Returns(true);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => reader.GetReadyDescriptorAsync("workflow", Guid.CreateVersion7(), Guid.CreateVersion7()));
        Assert.AreEqual(0, query.ReceivedCalls().Count());
    }
}
