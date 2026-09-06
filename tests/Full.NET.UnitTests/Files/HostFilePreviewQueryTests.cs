using Full.NET.Abstractions.Results;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.ManageHostFiles;
using Full.NET.Modules.Files.Persistence;
using Full.NET.Modules.Files.Storage;
using NSubstitute;

namespace Full.NET.UnitTests.Files;

[TestClass]
public sealed class HostFilePreviewQueryTests
{
    [TestMethod]
    public async Task OpenPreview_rejects_unsupported_content_type()
    {
        var fileId = Guid.CreateVersion7();
        var queryExecutor = Substitute.For<Full.NET.Data.Abstractions.IQueryExecutor>();
        queryExecutor.QuerySingleOrDefaultAsync<HostFileDetailRecord>(
                HostFileSql.FindActiveById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(FilesTestSupport.CreateDetailRecord(
                fileId,
                "binary.bin",
                "application/octet-stream",
                1,
                LocalHostFileBlobStorage.Key,
                "host/test.bin",
                null,
                DateTimeOffset.UtcNow,
                Guid.CreateVersion7()));
        var service = FilesTestSupport.CreateFileQueryService(queryExecutor);

        var result = await service.OpenPreviewAsync(fileId, CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(FilesErrorCodes.PreviewNotSupported, result.Error?.Code);
    }
}
