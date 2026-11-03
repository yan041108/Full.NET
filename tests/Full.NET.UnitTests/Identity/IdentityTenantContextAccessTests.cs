using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ChangeSessionContext;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

/// <summary>平台显式授权与自助成员范围分开；默认切换权限不能产生全目录准入。</summary>
[TestClass]
public sealed class IdentityTenantContextAccessTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Explicit_platform_grant_is_distinct_from_member_scope(bool global)
    {
        var query = Substitute.For<IQueryExecutor>();
        var tenantId = Guid.CreateVersion7();
        query.QuerySingleOrDefaultAsync<int>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(global ? 1 : 0);
        query.QueryAsync<Guid>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new[] { tenantId });
        var access = await new IdentityTenantContextAccessReader(query).ReadAsync(Guid.CreateVersion7());
        Assert.AreEqual(global, access.AllTenants);
        CollectionAssert.AreEqual(global ? Array.Empty<Guid>() : new[] { tenantId }, access.TenantIds.ToArray());
        await query.Received(global ? 0 : 1).QueryAsync<Guid>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }
}
