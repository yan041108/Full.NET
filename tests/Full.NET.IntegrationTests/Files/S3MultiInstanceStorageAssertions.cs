using System.Collections.Concurrent;
using Full.NET.Modules.Files.Storage;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Files;

/// <summary>
/// 用共享内存替身验证两实例共用同一对象命名空间时可互相读写删除。
/// 真实 MinIO/S3 容器未在本机启动时，不把容器验收写为通过。
/// </summary>
internal static class S3MultiInstanceStorageAssertions
{
    public static async Task VerifySharedNamespaceAsync(
        CancellationToken cancellationToken = default)
    {
        var shared = new ConcurrentDictionary<string, byte[]>(StringComparer.Ordinal);
        var options = new S3FileStorageOptions
        {
            EndpointMode = S3EndpointMode.Custom,
            ServiceUrl = "https://s3.test.local",
            Region = "us-east-1",
            BucketName = "fullnet-shared",
            ForcePathStyle = true,
        };
        var monitor = new StaticOptionsMonitor(options);
        using var instanceA = new S3HostFileBlobStorage(
            monitor,
            new InMemoryS3BlobClient(shared));
        using var instanceB = new S3HostFileBlobStorage(
            monitor,
            new InMemoryS3BlobClient(shared));

        const string key = "host/2026/08/shared-object";
        byte[] payload = [10, 20, 30, 40];
        await instanceA.SaveAsync(key, new MemoryStream(payload), cancellationToken);
        Assert.IsTrue(await instanceB.ExistsAsync(key, cancellationToken));

        await using (var read = await instanceB.OpenReadAsync(key, cancellationToken))
        {
            using var buffer = new MemoryStream();
            await read.CopyToAsync(buffer, cancellationToken);
            CollectionAssert.AreEqual(payload, buffer.ToArray());
        }

        await instanceB.DeleteAsync(key, cancellationToken);
        Assert.IsFalse(await instanceA.ExistsAsync(key, cancellationToken));
        await instanceA.DeleteAsync(key, cancellationToken);
    }

    private sealed class StaticOptionsMonitor(S3FileStorageOptions current)
        : IOptionsMonitor<S3FileStorageOptions>
    {
        public S3FileStorageOptions CurrentValue { get; } = current;

        public S3FileStorageOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<S3FileStorageOptions, string?> listener) => null;
    }

    private sealed class InMemoryS3BlobClient(
        ConcurrentDictionary<string, byte[]> store) : IS3BlobClient
    {
        public async Task PutAsync(
            string bucketName,
            string objectKey,
            Stream content,
            CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            store[Compose(bucketName, objectKey)] = buffer.ToArray();
        }

        public Task CopyAsync(
            string bucketName,
            string sourceKey,
            string destinationKey,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!store.TryGetValue(Compose(bucketName, sourceKey), out var bytes))
            {
                throw new FileNotFoundException("source missing", sourceKey);
            }

            if (!store.TryAdd(Compose(bucketName, destinationKey), bytes))
            {
                throw new IOException($"destination '{destinationKey}' already exists.");
            }

            return Task.CompletedTask;
        }

        public Task DeleteAsync(
            string bucketName,
            string objectKey,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            store.TryRemove(Compose(bucketName, objectKey), out _);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(
            string bucketName,
            string objectKey,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(store.ContainsKey(Compose(bucketName, objectKey)));
        }

        public Task<Stream> OpenReadAsync(
            string bucketName,
            string objectKey,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!store.TryGetValue(Compose(bucketName, objectKey), out var bytes))
            {
                throw new FileNotFoundException("Stored blob was not found.", objectKey);
            }

            return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
        }

        private static string Compose(string bucket, string key) => bucket + "\n" + key;
    }
}

[TestClass]
public sealed class S3MultiInstanceStorageTests
{
    [TestMethod]
    public Task Shared_in_memory_namespace_supports_cross_instance_read_and_delete() =>
        S3MultiInstanceStorageAssertions.VerifySharedNamespaceAsync();
}
