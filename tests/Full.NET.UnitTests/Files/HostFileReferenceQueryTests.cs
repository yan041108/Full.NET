using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.HostFileReferenceClaims;
using Full.NET.Modules.Files.Features.ManageHostFiles;
using Full.NET.Modules.Files.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Files;

[TestClass]
public sealed class HostFileReferenceQueryTests
{
    [TestMethod]
    public async Task ListReferences_returns_claim_rows_without_cross_module_joins()
    {
        var fileId = Guid.CreateVersion7();
        var claimId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        var detail = FilesTestSupport.CreateDetailRecord(
            fileId,
            "claim.txt",
            "text/plain",
            4,
            "local",
            $"host/{fileId:N}",
            null,
            now,
            Guid.CreateVersion7());
        var claim = new HostFileReferenceClaimRecord(
            claimId,
            "document-version:abc",
            fileId,
            HostFileReferenceClaimConsumerModules.Document,
            Guid.CreateVersion7(),
            HostFileReferenceClaimStates.Active,
            null,
            4,
            now,
            now,
            now,
            null);
        var queryExecutor = Substitute.For<IQueryExecutor>();
        queryExecutor.QuerySingleOrDefaultAsync<HostFileDetailRecord>(
                HostFileSql.FindActiveById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(detail);
        queryExecutor.QuerySingleOrDefaultAsync<long>(
                HostFileReferenceClaimSql.CountByFileId,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1L);
        queryExecutor.QueryAsync<HostFileReferenceClaimRecord>(
                HostFileReferenceClaimSql.ListByFileIdSqlServer,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(new[] { claim });
        var service = FilesTestSupport.CreateFileQueryService(
            queryExecutor,
            DatabaseProvider.SqlServer);

        var result = await service.ListReferencesAsync(fileId, 1, 20, CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, result.Value!.Total);
        Assert.AreEqual(claimId, result.Value.Items[0].Id);
        Assert.AreEqual(HostFileReferenceClaimConsumerModules.Document, result.Value.Items[0].ConsumerModule);
    }
}
