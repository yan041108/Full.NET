namespace Full.NET.Modules.Files.Contracts;

public interface IHostFileReferenceReader
{
    Task<HostFileReference?> GetReadyReferenceAsync(Guid fileId, CancellationToken cancellationToken = default);
}

public sealed record HostFileReference(Guid FileId, long SizeBytes, string? ContentHash);
