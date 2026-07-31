using Full.NET.Modules.Files.Storage;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Files;

[TestClass]
public sealed class LocalHostFileBlobStorageTests
{
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
