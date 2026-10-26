using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Features.TenantResourceFiles;
using Full.NET.Modules.Files.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Files;

/// <summary>描述 Port 的租户与资源守卫不能被消费模块的文件 UUID 绕过。</summary>
[TestClass]
public sealed class TenantResourceFileDescriptorReaderTests
{
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
        var reader = new TenantResourceFileDescriptorReader(query, tenant, Substitute.For<IDataTransactionState>());
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
        var reader = new TenantResourceFileDescriptorReader(query, tenant, transaction);
        await Assert.ThrowsExactlyAsync<TenantContextMissingException>(() => reader.GetReadyDescriptorAsync("workflow", Guid.CreateVersion7(), Guid.CreateVersion7()));
        transaction.HasTransaction.Returns(true);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => reader.GetReadyDescriptorAsync("workflow", Guid.CreateVersion7(), Guid.CreateVersion7()));
        Assert.AreEqual(0, query.ReceivedCalls().Count());
    }
}
