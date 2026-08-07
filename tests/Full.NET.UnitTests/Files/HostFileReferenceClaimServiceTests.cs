using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.HostFileReferenceClaims;
using Full.NET.Modules.Files.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Files;

[TestClass]
public sealed class HostFileReferenceClaimServiceTests
{
    [TestMethod]
    public async Task Claim_is_idempotent_for_matching_payload()
    {
        var fileId = Guid.CreateVersion7();
        var versionId = Guid.CreateVersion7();
        var existing = new HostFileReferenceClaimRecord(
            Guid.CreateVersion7(),
            HostFileReferenceClaimIdempotencyKeys.DocumentVersion(versionId),
            fileId,
            HostFileReferenceClaimConsumerModules.Document,
            versionId,
            HostFileReferenceClaimStates.Pending,
            "abc",
            4,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            null);
        var queryExecutor = Substitute.For<IQueryExecutor>();
        queryExecutor.QuerySingleOrDefaultAsync<HostFileReferenceClaimRecord>(
                HostFileReferenceClaimSql.FindByIdempotencyKey,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(existing);
        var service = CreateService(queryExecutor, Substitute.For<ICommandExecutor>());

        var result = await service.ClaimAsync(
            new HostFileReferenceClaimRequest(
                existing.IdempotencyKey,
                existing.ConsumerModule,
                existing.ConsumerReferenceId,
                existing.FileId));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(existing.Id, result.Value!.ClaimId);
        Assert.AreEqual(HostFileReferenceClaimStates.Pending, result.Value.State);
    }

    [TestMethod]
    public async Task Claim_rejects_payload_conflict_for_same_idempotency_key()
    {
        var fileId = Guid.CreateVersion7();
        var versionId = Guid.CreateVersion7();
        var existing = new HostFileReferenceClaimRecord(
            Guid.CreateVersion7(),
            HostFileReferenceClaimIdempotencyKeys.DocumentVersion(versionId),
            fileId,
            HostFileReferenceClaimConsumerModules.Document,
            versionId,
            HostFileReferenceClaimStates.Pending,
            "abc",
            4,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            null);
        var queryExecutor = Substitute.For<IQueryExecutor>();
        queryExecutor.QuerySingleOrDefaultAsync<HostFileReferenceClaimRecord>(
                HostFileReferenceClaimSql.FindByIdempotencyKey,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(existing);
        var service = CreateService(queryExecutor, Substitute.For<ICommandExecutor>());

        var result = await service.ClaimAsync(
            new HostFileReferenceClaimRequest(
                existing.IdempotencyKey,
                existing.ConsumerModule,
                existing.ConsumerReferenceId,
                Guid.CreateVersion7()));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(FilesErrorCodes.ClaimPayloadConflict, result.Error!.Code);
    }

    private static HostFileReferenceClaimService CreateService(
        IQueryExecutor queryExecutor,
        ICommandExecutor commandExecutor)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var idGenerator = Substitute.For<IIdGenerator>();
        idGenerator.NewId().Returns(Guid.CreateVersion7());
        return new HostFileReferenceClaimService(
            queryExecutor,
            commandExecutor,
            new ImmediateTransaction(),
            clock,
            idGenerator);
    }

    private sealed class ImmediateTransaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) =>
            action(cancellationToken);
    }
}