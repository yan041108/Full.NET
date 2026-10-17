using Full.NET.Modules.Files.Storage;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Files;

[TestClass]
public sealed class LocalHostFileBlobStorageTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Missing_local_object_maps_to_not_found(bool missingDirectory)
    {
        var rootPath = CreateRootPath();
        Directory.CreateDirectory(rootPath);
        try
        {
            var result = await FileStorageContentReader.OpenAsync(CreateStorage(rootPath),
                missingDirectory ? "absent/file" : "absent", CancellationToken.None);
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual("files.file.not_found", result.Error!.Code);
        }
        finally { Directory.Delete(rootPath, recursive: true); }
    }

    [TestMethod]
    public async Task Existing_directory_is_not_misreported_as_a_missing_blob()
    {
        var rootPath = CreateRootPath();
        Directory.CreateDirectory(Path.Combine(rootPath, "directory"));
        try
        {
            // File.Exists 对目录也返回 false；直接打开应保留操作系统的访问错误。
            await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() =>
                FileStorageContentReader.OpenAsync(CreateStorage(rootPath), "directory", CancellationToken.None));
        }
        finally { Directory.Delete(rootPath, recursive: true); }
    }

    [TestMethod]
    public async Task Invalid_local_path_is_not_misreported_as_a_missing_blob()
    {
        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            FileStorageContentReader.OpenAsync(CreateStorage(CreateRootPath()), "invalid\0path", CancellationToken.None));
    }

    [TestMethod]
    public async Task Local_open_transfers_the_complete_stream_to_the_caller()
    {
        var rootPath = CreateRootPath();
        Directory.CreateDirectory(rootPath);
        try
        {
            byte[] expected = [1, 2, 3];
            await File.WriteAllBytesAsync(Path.Combine(rootPath, "result"), expected);
            var result = await FileStorageContentReader.OpenAsync(CreateStorage(rootPath), "result", CancellationToken.None);
            Assert.IsTrue(result.IsSuccess);
            await using var stream = result.Value!;
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            CollectionAssert.AreEqual(expected, buffer.ToArray());
        }
        finally { Directory.Delete(rootPath, recursive: true); }
    }

    [TestMethod]
    public async Task Successful_save_publishes_complete_content_without_staging_files()
    {
        var rootPath = CreateRootPath();
        Directory.CreateDirectory(rootPath);
        try
        {
            var storage = CreateStorage(rootPath);
            byte[] expected = [1, 2, 3, 4];
            await using var content = new MemoryStream(expected);

            await storage.SaveAsync(
                "host/2026/07/complete",
                content,
                CancellationToken.None);

            var files = Directory.GetFiles(
                rootPath,
                "*",
                SearchOption.AllDirectories);
            Assert.HasCount(1, files);
            CollectionAssert.AreEqual(
                expected,
                await File.ReadAllBytesAsync(files[0]));
            Assert.IsFalse(files[0].EndsWith(
                ".uploading",
                StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [TestMethod]
    public async Task Existing_final_blob_is_not_overwritten_and_staging_is_removed()
    {
        var rootPath = CreateRootPath();
        var finalPath = Path.Combine(
            rootPath,
            "host",
            "2026",
            "07",
            "existing");
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        byte[] original = [9, 8, 7];
        await File.WriteAllBytesAsync(finalPath, original);
        try
        {
            var storage = CreateStorage(rootPath);
            await using var replacement = new MemoryStream([1, 2, 3]);

            _ = await Assert.ThrowsAsync<IOException>(
                () => storage.SaveAsync(
                    "host/2026/07/existing",
                    replacement,
                    CancellationToken.None));

            CollectionAssert.AreEqual(
                original,
                await File.ReadAllBytesAsync(finalPath));
            Assert.HasCount(
                1,
                Directory.GetFiles(
                    rootPath,
                    "*",
                    SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [TestMethod]
    public async Task Canceled_save_does_not_leave_a_partial_final_blob()
    {
        var rootPath = CreateRootPath();
        Directory.CreateDirectory(rootPath);
        try
        {
            var storage = CreateStorage(rootPath);
            await using var content = new MemoryStream([1, 2, 3, 4]);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            _ = await Assert.ThrowsAsync<OperationCanceledException>(
                () => storage.SaveAsync(
                    "host/2026/07/canceled",
                    content,
                    cancellation.Token));

            Assert.AreEqual(
                0,
                Directory.GetFiles(
                    rootPath,
                    "*",
                    SearchOption.AllDirectories).Length);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    private static string CreateRootPath() =>
        Path.Combine(
            Path.GetTempPath(),
            $"fullnet-files-atomic-{Guid.CreateVersion7():N}");

    private static LocalHostFileBlobStorage CreateStorage(string rootPath) =>
        new(Options.Create(new LocalFileStorageOptions
        {
            RootPath = rootPath,
            MaxUploadBytes = 1024,
        }));
}
