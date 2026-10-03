using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Organization.TenantUnits;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Organization;

/// <summary>不完整上下文必须在数据库访问前拒绝，不能退回全局组织目录。</summary>
[TestClass]
public sealed class CurrentTenantOrganizationUnitDirectoryTests
{
    [TestMethod]
    [DataRow(true, true)]
    [DataRow(false, false)]
    [DataRow(false, true)]
    public async Task Host_or_incomplete_tenant_never_queries_the_directory(bool host, bool available)
    {
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.IsHost.Returns(host);
        tenant.IsAvailable.Returns(available);
        tenant.Id.Returns((Guid?)null);
        var query = Substitute.For<IQueryExecutor>();
        var directory = new CurrentTenantOrganizationUnitDirectory(query, Options.Create(new DatabaseOptions()), tenant);
        await Assert.ThrowsAsync<TenantContextMissingException>(() => directory.ListActiveUnitsAsync(1, 20));
        await Assert.ThrowsAsync<TenantContextMissingException>(() => directory.FindActiveUnitsAsync([]));
        await Assert.ThrowsAsync<TenantContextMissingException>(() => directory.ListActiveUnitIdsForUserAsync(Guid.CreateVersion7()));
        Assert.IsFalse(query.ReceivedCalls().Any());
    }
}
