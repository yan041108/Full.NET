using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.HostFileReferenceClaims;
using Full.NET.Modules.Files.Features.ManageHostFiles;
using Full.NET.Modules.Files.Features.ManageHostFolders;
using Full.NET.Modules.Files.Persistence;
using Full.NET.Modules.Files.Storage;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Files;

[TestClass]
public sealed class HostFileMetadataUpdateTests
{
    [TestMethod]
    public async Task UpdateMetadata_moves_file_to_folder_when_revision_matches()
    {
        var fileId = Guid.CreateVersion7();
        var folderId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        var detail = FilesTestSupport.CreateDetailRecord(
            fileId,
            "before.txt",
            "text/plain",
            4,
            LocalHostFileBlobStorage.Key,
            $"host/{fileId:N}",
            null,
            now,
            actorId,
            revision: 3);
        var queryExecutor = Substitute.For<IQueryExecutor>();
        queryExecutor.QuerySingleOrDefaultAsync<HostFileDetailRecord>(
                HostFileSql.FindActiveById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(
                detail,
                FilesTestSupport.CreateDetailRecord(
                    fileId,
                    "after.txt",
                    "text/plain",
                    4,
                    LocalHostFileBlobStorage.Key,
                    $"host/{fileId:N}",
                    null,
                    now,
                    actorId,
                    folderId,
                    4,
                    now,
                    actorId));
        queryExecutor.QuerySingleOrDefaultAsync<long>(
                HostFolderSql.ExistsActiveById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1L);
        var commandExecutor = Substitute.For<ICommandExecutor>();
        commandExecutor.ExecuteAsync(
                HostFileSql.UpdateMetadata,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        var service = new HostFileManagementService(
            commandExecutor,
            new ImmediateMetadataTransaction(),
            FilesTestSupport.CreateFileQueryService(queryExecutor),
            FilesTestSupport.CreateFolderQueryService(),
            Substitute.For<IHostFileReferenceClaimService>(),
            new FileStorageProviderRegistry(
                [new RecordingBlobStorage()],
                Options.Create(new FileStorageOptions
                {
                    DefaultProviderKey = LocalHostFileBlobStorage.Key,
                })),
            Substitute.For<IClock>(),
            Substitute.For<IIdGenerator>(),
            Options.Create(new LocalFileStorageOptions
            {
                RootPath = "unused",
                MaxUploadBytes = 1024,
            }));

        var result = await service.UpdateMetadataAsync(
            fileId,
            actorId,
            new UpdateHostFileMetadataRequest(3, "after.txt", folderId),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(folderId, result.Value!.FolderId);
        Assert.AreEqual("after.txt", result.Value.OriginalFileName);
        Assert.AreEqual(4, result.Value.Revision);
    }

    private sealed class ImmediateMetadataTransaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) =>
            action(cancellationToken);
    }

    private sealed class RecordingBlobStorage : IFileStorageProvider
    {
        public string ProviderKey => LocalHostFileBlobStorage.Key;

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream());

        public Task SaveAsync(
            string storageKey,
            Stream content,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
