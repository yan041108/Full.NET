using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.HostFileReferenceClaims;
using NSubstitute;

namespace Full.NET.UnitTests.Files;

/// <summary>释放失败必须返回投递失败，重试可继续，且恢复调用方租户上下文。</summary>
[TestClass]
public sealed class HostFileReleaseDeliveryTests
{
    [TestMethod]
    public async Task Failed_release_is_retried_and_scope_is_restored()
    {
        var tenant = new CurrentTenantAccessor();
        ((ICurrentTenantContextWriter)tenant).SetTenant(new(Guid.NewGuid(), "tenant", "Tenant"));
        var tenantId = tenant.Id;
        var serializer = Substitute.For<IIntegrationEventSerializer>();
        serializer.Deserialize<HostFileReferenceReleaseRequested>(Arg.Any<ReadOnlyMemory<byte>>()).Returns(new HostFileReferenceReleaseRequested("claim"));
        var claims = Substitute.For<IHostFileReferenceClaimService>();
        claims.ReleaseAsync("claim", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            Assert.IsTrue(tenant.IsHost);
            return Result<bool>.Failure(new Error("files.retry", "retry", ErrorType.Conflict));
        });
        Assert.IsTrue(Full.NET.Messaging.Abstractions.MessagingNames.MessageTypePattern.IsMatch(HostFileReferenceReleaseRequested.EventType));
        var handler = new HostFileReferenceReleaseRequestedHandler(serializer, claims, (ICurrentTenantContextWriter)tenant);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => handler.HandleAsync(ReadOnlyMemory<byte>.Empty, default));
        Assert.AreEqual(tenantId, tenant.Id);
        claims.ReleaseAsync("claim", Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));
        await handler.HandleAsync(ReadOnlyMemory<byte>.Empty, default);
        Assert.AreEqual(tenantId, tenant.Id);
        await claims.Received(2).ReleaseAsync("claim", Arg.Any<CancellationToken>());
    }
}
