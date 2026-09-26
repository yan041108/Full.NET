using Full.NET.Abstractions.Results;
using Full.NET.Modules.Files.Contracts;

namespace Full.NET.Modules.Files.Features.TenantResourceFiles;

internal sealed class NullTenantFileStorageQuotaPort : ITenantFileStorageQuotaPort
{
    public Task<Result<bool>> TryReserveAsync(
        Guid tenantId,
        string operationId,
        long byteCount,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<bool>.Success(true));

    public Task<Result<bool>> ConfirmAsync(
        Guid tenantId,
        string operationId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<bool>.Success(true));

    public Task<Result<bool>> ReleaseAsync(
        Guid tenantId,
        string operationId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<bool>.Success(true));
}
