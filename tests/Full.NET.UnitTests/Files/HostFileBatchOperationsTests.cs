using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.ManageHostFiles;
using Full.NET.Modules.Files.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Files;

[TestClass]
public sealed class HostFileBatchOperationsTests
{
    [TestMethod]
    public async Task BatchUpload_rejects_more_than_maximum_files()
    {
        var service = CreateManagementService();
        var files = Enumerable.Range(0, HostFileBatchLimits.MaxUploadCount + 1)
            .Select(_ => CreateFormFile("file.txt"))
            .ToArray();

        var result = await service.BatchUploadAsync(
            Guid.CreateVersion7(),
            files,
            null,
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(FilesErrorCodes.InvalidBatch, result.Error?.Code);
    }

    [TestMethod]
    public async Task BatchDelete_rejects_empty_file_ids()
    {
        var service = CreateManagementService();

        var result = await service.BatchDeleteAsync(
            new BatchDeleteHostFilesRequest([]),
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(FilesErrorCodes.InvalidBatch, result.Error?.Code);
    }

    [TestMethod]
    public void Preview_supports_text_image_and_pdf_only()
    {
        Assert.IsTrue(HostFilePreviewSupport.IsSupportedContentType("text/plain"));
        Assert.IsTrue(HostFilePreviewSupport.IsSupportedContentType("image/png"));
        Assert.IsTrue(HostFilePreviewSupport.IsSupportedContentType("application/pdf"));
        Assert.IsFalse(HostFilePreviewSupport.IsSupportedContentType("application/octet-stream"));
        Assert.IsFalse(HostFilePreviewSupport.IsSupportedContentType("text/html"));
    }

    private static HostFileManagementService CreateManagementService() =>
        new(
            Substitute.For<ICommandExecutor>(),
            Substitute.For<ICommandTransaction>(),
            FilesTestSupport.CreateFileQueryService(
                Substitute.For<IQueryExecutor>()),
            FilesTestSupport.CreateFolderQueryService(),
            Substitute.For<IHostFileReferenceClaimService>(),
            FilesTestSupport.CreateRegistry(
                new LocalHostFileBlobStorage(Options.Create(new LocalFileStorageOptions
                {
                    RootPath = Path.GetTempPath(),
                    MaxUploadBytes = 1024,
                }))),
            Substitute.For<Full.NET.Abstractions.Time.IClock>(),
            Substitute.For<Full.NET.Abstractions.Ids.IIdGenerator>(),
            Microsoft.Extensions.Options.Options.Create(
                new Full.NET.Modules.Files.Storage.LocalFileStorageOptions
                {
                    RootPath = "unused",
                    MaxUploadBytes = 1024,
                }));

    private static IFormFile CreateFormFile(string fileName)
    {
        var file = Substitute.For<IFormFile>();
        file.FileName.Returns(fileName);
        file.ContentType.Returns("text/plain");
        file.Length.Returns(1);
        file.OpenReadStream().Returns(_ => new MemoryStream([1]));
        return file;
    }
}
