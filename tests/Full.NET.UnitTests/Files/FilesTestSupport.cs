using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.ManageHostFiles;
using Full.NET.Modules.Files.Features.ManageHostFolders;
using Full.NET.Modules.Files.Persistence;
using Full.NET.Modules.Files.Storage;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Files;

/// <summary>Files 模块单元测试共享夹具。</summary>
internal static class FilesTestSupport
{
    public static HostFileQueryService CreateFileQueryService(
        IQueryExecutor queryExecutor,
        DatabaseProvider provider = DatabaseProvider.SqlServer)
    {
        var contentReader = Substitute.For<IHostFileContentReader>();
        return new HostFileQueryService(
            queryExecutor,
            contentReader,
            Options.Create(new DatabaseOptions { Provider = provider }));
    }

    public static HostFolderQueryService CreateFolderQueryService(bool exists = true)
    {
        var queryExecutor = Substitute.For<IQueryExecutor>();
        queryExecutor.QuerySingleOrDefaultAsync<long>(
                Arg.Any<SqlStatement>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(exists ? 1L : 0L);
        return new HostFolderQueryService(queryExecutor);
    }

    public static HostFileDetailRecord CreateDetailRecord(
        Guid fileId,
        string originalFileName,
        string contentType,
        long sizeBytes,
        string providerKey,
        string storageKey,
        string? contentHash,
        DateTimeOffset createdAtUtc,
        Guid createdByUserId,
        Guid? folderId = null,
        long revision = 0,
        DateTimeOffset? updatedAtUtc = null,
        Guid? updatedByUserId = null) =>
        new(
            fileId,
            originalFileName,
            contentType,
            sizeBytes,
            providerKey,
            storageKey,
            contentHash,
            createdAtUtc,
            createdByUserId,
            folderId,
            revision,
            updatedAtUtc,
            updatedByUserId);

    public static FileStorageProviderRegistry CreateRegistry(
        params IFileStorageProvider[] providers) =>
        new(
            providers,
            Options.Create(new FileStorageOptions
            {
                DefaultProviderKey = providers[0].ProviderKey,
            }));
}
