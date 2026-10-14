using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.HostFileReferences;
using Full.NET.Modules.Files.Persistence;
using Full.NET.Modules.Files.Storage;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Files;

/// <summary>Host 内容端口复用存储读取边界，不把缺失对象转成服务器错误或跳过元数据授权。</summary>
[TestClass]
public sealed class HostFileContentReaderTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Missing_blob_returns_stable_not_found(bool directory)
    {
        var (reader, storage) = Create();
        Exception error = directory ? new DirectoryNotFoundException("internal") : new FileNotFoundException("internal", "private-key");
        storage.OpenReadAsync("private-key", Arg.Any<CancellationToken>()).Returns(Task.FromException<Stream>(error));
        var result = await reader.OpenReadyContentAsync(Guid.NewGuid());
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(FilesErrorCodes.FileNotFound, result.Error!.Code);
        Assert.AreEqual(ErrorType.NotFound, result.Error.Type); Assert.IsFalse(result.Error.Message.Contains("private", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Missing_metadata_does_not_access_object_storage()
    {
        var (reader, storage) = Create(false);
        var result = await reader.OpenReadyContentAsync(Guid.NewGuid());
        Assert.IsFalse(result.IsSuccess);
        await storage.DidNotReceive().OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Real_storage_permission_failure_keeps_its_failure_semantics()
    {
        var (reader, storage) = Create();
        storage.OpenReadAsync("private-key", Arg.Any<CancellationToken>()).Returns(Task.FromException<Stream>(new UnauthorizedAccessException()));
        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => reader.OpenReadyContentAsync(Guid.NewGuid()));
    }

    [TestMethod]
    public async Task Ready_content_transfers_original_stream_and_download_metadata()
    {
        var (reader, storage) = Create(); var stream = new MemoryStream([1]);
        storage.OpenReadAsync("private-key", Arg.Any<CancellationToken>()).Returns(stream);
        var result = await reader.OpenReadyContentAsync(Guid.NewGuid());
        Assert.IsTrue(result.IsSuccess); Assert.AreSame(stream, result.Value!.Content);
        Assert.AreEqual("result.pdf", result.Value.OriginalFileName); Assert.AreEqual("application/pdf", result.Value.ContentType);
        await result.Value.Content.DisposeAsync();
    }

    private static (HostFileContentReader Reader, IFileStorageProvider Storage) Create(bool metadata = true)
    {
        var queries = Substitute.For<IQueryExecutor>();
        queries.QuerySingleOrDefaultAsync<HostFileDetailRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(metadata ? new HostFileDetailRecord(Guid.NewGuid(), "result.pdf", "application/pdf", 1, "test", "private-key", null, DateTimeOffset.UtcNow, Guid.NewGuid(), null, 1, null, null) : null);
        var storage = Substitute.For<IFileStorageProvider>(); storage.ProviderKey.Returns("test");
        return (new HostFileContentReader(queries, new FileStorageProviderRegistry([storage], Options.Create(new FileStorageOptions { DefaultProviderKey = "test" }))), storage);
    }
}
