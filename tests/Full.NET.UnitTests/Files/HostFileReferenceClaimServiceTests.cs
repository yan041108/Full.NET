using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.HostFileReferenceClaims;
using Full.NET.Modules.Files.Persistence;
using Microsoft.Extensions.Options;
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

    [TestMethod]
    public async Task Claim_rejects_reuse_after_matching_claim_was_released()
    {
        var fileId = Guid.CreateVersion7();
        var versionId = Guid.CreateVersion7();
        var existing = new HostFileReferenceClaimRecord(
            Guid.CreateVersion7(),
            HostFileReferenceClaimIdempotencyKeys.DocumentVersion(versionId),
            fileId,
            HostFileReferenceClaimConsumerModules.Document,
            versionId,
            HostFileReferenceClaimStates.Released,
            "abc",
            4,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            DateTimeOffset.UtcNow);
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

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(FilesErrorCodes.InvalidClaim, result.Error!.Code);
    }

    [TestMethod]
    public async Task Claim_fails_when_file_becomes_unavailable_before_pending_insert()
    {
        var fileId = Guid.CreateVersion7();
        var versionId = Guid.CreateVersion7();
        var queryExecutor = Substitute.For<IQueryExecutor>();
        queryExecutor.QuerySingleOrDefaultAsync<HostFileReferenceClaimRecord>(
                HostFileReferenceClaimSql.FindByIdempotencyKey,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns((HostFileReferenceClaimRecord?)null);
        queryExecutor.QuerySingleOrDefaultAsync<Guid>(
                Arg.Any<SqlStatement>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(fileId);
        queryExecutor.QuerySingleOrDefaultAsync<HostFileDetailRecord>(
                HostFileSql.FindActiveById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(new HostFileDetailRecord(
                fileId,
                "claim.txt",
                "text/plain",
                4,
                "local",
                $"host/{fileId:N}",
                "abc",
                DateTimeOffset.UtcNow,
                Guid.CreateVersion7()));
        var commandExecutor = Substitute.For<ICommandExecutor>();
        commandExecutor.ExecuteAsync(
                HostFileReferenceClaimSql.InsertPending,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(0);
        var service = CreateService(queryExecutor, commandExecutor);

        var result = await service.ClaimAsync(
            new HostFileReferenceClaimRequest(
                HostFileReferenceClaimIdempotencyKeys.DocumentVersion(versionId),
                HostFileReferenceClaimConsumerModules.Document,
                versionId,
                fileId));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(FilesErrorCodes.FileNotFound, result.Error!.Code);
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
            idGenerator,
            Options.Create(new DatabaseOptions
            {
                Provider = DatabaseProvider.SqlServer,
            }));
    }

    private sealed class ImmediateTransaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) =>
            action(cancellationToken);
    }
}
