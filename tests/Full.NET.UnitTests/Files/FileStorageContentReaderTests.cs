using Full.NET.Abstractions.Results;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Storage;
using NSubstitute;

namespace Full.NET.UnitTests.Files;

/// <summary>缺失对象、真实存储故障和取消具有独立语义，读取成功后流所有权交给调用方。</summary>
[TestClass]
public sealed class FileStorageContentReaderTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Missing_object_returns_not_found_without_internal_path(bool directory)
    {
        var provider = Substitute.For<IFileStorageProvider>();
        IOException exception = directory ? new DirectoryNotFoundException("private-path") : new FileNotFoundException("private-path", "private-key");
        provider.OpenReadAsync("private-key", Arg.Any<CancellationToken>()).Returns(Task.FromException<Stream>(exception));
        var result = await FileStorageContentReader.OpenAsync(provider, "private-key", default);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(FilesErrorCodes.FileNotFound, result.Error!.Code);
        Assert.AreEqual(ErrorType.NotFound, result.Error.Type);
        Assert.IsFalse(result.Error.Message.Contains("private", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public async Task Storage_failure_and_cancellation_are_not_disguised_as_missing(int kind)
    {
        Exception exception = kind switch { 0 => new IOException("io"), 1 => new UnauthorizedAccessException("permission"), 2 => new InvalidOperationException("configuration"), _ => new OperationCanceledException() };
        var provider = Substitute.For<IFileStorageProvider>();
        provider.OpenReadAsync("key", Arg.Any<CancellationToken>()).Returns(Task.FromException<Stream>(exception));
        try { await FileStorageContentReader.OpenAsync(provider, "key", default); Assert.Fail("The original failure must propagate."); }
        catch (Exception actual) when (ReferenceEquals(actual, exception)) { }
    }

    [TestMethod]
    public async Task Early_cancellation_does_not_access_storage()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var provider = Substitute.For<IFileStorageProvider>();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => FileStorageContentReader.OpenAsync(provider, "key", cancellation.Token));
        await provider.DidNotReceive().OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Cancellation_wins_over_missing_object()
    {
        using var cancellation = new CancellationTokenSource();
        var provider = Substitute.For<IFileStorageProvider>();
        provider.OpenReadAsync("key", Arg.Any<CancellationToken>()).Returns(_ => { cancellation.Cancel(); return Task.FromException<Stream>(new FileNotFoundException()); });
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => FileStorageContentReader.OpenAsync(provider, "key", cancellation.Token));
    }

    [TestMethod]
    public async Task Late_cancellation_disposes_the_new_stream()
    {
        using var cancellation = new CancellationTokenSource();
        var stream = new MemoryStream([1]); var provider = Substitute.For<IFileStorageProvider>();
        provider.OpenReadAsync("key", Arg.Any<CancellationToken>()).Returns(_ => { cancellation.Cancel(); return Task.FromResult<Stream>(stream); });
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => FileStorageContentReader.OpenAsync(provider, "key", cancellation.Token));
        Assert.IsFalse(stream.CanRead);
    }

    [TestMethod]
    public async Task Disposal_failure_is_not_converted_to_missing_content()
    {
        using var cancellation = new CancellationTokenSource();
        var stream = new FailingDisposeStream(); var provider = Substitute.For<IFileStorageProvider>();
        provider.OpenReadAsync("key", Arg.Any<CancellationToken>()).Returns(_ => { cancellation.Cancel(); return Task.FromResult<Stream>(stream); });
        await Assert.ThrowsExactlyAsync<FileNotFoundException>(() => FileStorageContentReader.OpenAsync(provider, "key", cancellation.Token));
    }

    private sealed class FailingDisposeStream : MemoryStream
    {
        public override ValueTask DisposeAsync() => ValueTask.FromException(new FileNotFoundException("dispose-failure"));
    }

    [TestMethod]
    public async Task Successful_read_transfers_stream_ownership_without_existence_probe()
    {
        var stream = new MemoryStream([1, 2]); var provider = Substitute.For<IFileStorageProvider>();
        provider.OpenReadAsync("key", Arg.Any<CancellationToken>()).Returns(stream);
        var result = await FileStorageContentReader.OpenAsync(provider, "key", default);
        Assert.IsTrue(result.IsSuccess); Assert.AreSame(stream, result.Value); Assert.IsTrue(stream.CanRead);
        await provider.Received(1).OpenReadAsync("key", Arg.Any<CancellationToken>());
        await provider.DidNotReceive().ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await result.Value!.DisposeAsync(); Assert.IsFalse(stream.CanRead);
    }
}
