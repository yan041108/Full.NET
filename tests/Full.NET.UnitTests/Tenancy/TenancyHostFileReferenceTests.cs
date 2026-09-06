using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Tenancy.Features.HostFileReferences;
using Full.NET.Modules.Tenancy.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Tenancy;

/// <summary>验证 Tenancy Logo claim 探测与保留贡献者。</summary>
[TestClass]
public sealed class TenancyHostFileReferenceTests
{
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid FileId = Guid.CreateVersion7();

    [TestMethod]
    public void Probe_declares_tenancy_consumer_module()
    {
        var probe = new TenancyTenantLogoReferenceProbe(Substitute.For<IQueryExecutor>());
        Assert.AreEqual(HostFileReferenceClaimConsumerModules.Tenancy, probe.ConsumerModule);
    }

    [TestMethod]
    public async Task Probe_returns_exists_when_logo_is_bound()
    {
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<int>(
                TenantSql.TenantLogoExists,
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        var probe = new TenancyTenantLogoReferenceProbe(query);

        var result = await probe.ProbeReferenceAsync(TenantId, FileId);

        Assert.AreEqual(HostFileReferenceClaimProbeOutcome.Exists, result.Outcome);
    }

    [TestMethod]
    public async Task Retention_blocks_delete_when_logo_is_referenced()
    {
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<int>(
                TenantSql.IsTenantLogoReferenced,
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        var contributor = new TenancyHostFileRetentionContributor(query);

        var referenced = await contributor.IsFileReferencedAsync(FileId);

        Assert.IsTrue(referenced);
    }
}
