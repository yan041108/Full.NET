using Full.NET.Abstractions.Results;
using Full.NET.Modules.EnterpriseRequest.Contracts;

namespace Full.NET.Modules.EnterpriseRequest.Generated;

/// <summary>样例单据的领域约束独立于生成产物，API 与后台导入共用。</summary>
internal sealed partial class EnterpriseRequestManagementService
{
    partial void ValidateCreateDomain(CreateEnterpriseRequestRequest request, ref Error? error)
    {
        if (!IsDraft(request.Status)) error = InvalidStatus();
    }

    partial void ValidateUpdateDomain(Guid id, UpdateEnterpriseRequestRequest request,
        CancellationToken cancellationToken, ref Task<Error?>? validation)
    {
        validation = ValidateDraftUpdateAsync(id, request, cancellationToken);
    }

    private async Task<Error?> ValidateDraftUpdateAsync(Guid id,
        UpdateEnterpriseRequestRequest request, CancellationToken cancellationToken)
    {
        if (!IsDraft(request.Status)) return InvalidStatus();
        var existing = await queries.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (!existing.IsSuccess) return existing.Error;
        // 请求版本先绑定本次领域快照，不能让调用方猜测未来版本命中并发提交后的状态。
        if (request.Version != existing.Value!.Version) return InvalidVersion();
        // 相同快照之后发生提交会增加版本，生成 SQL 的版本 CAS 继续拒绝覆盖。
        return IsDraft(existing.Value!.Status) ? null : InvalidStatus();
    }

    partial void ValidateDeleteDomain(EnterpriseRequestResponse existing, long? expectedVersion, ref Error? error)
    {
        if (expectedVersion != existing.Version) error = InvalidVersion();
        else if (!IsDraft(existing.Status)) error = InvalidStatus();
    }

    private static bool IsDraft(string status) =>
        string.Equals(status, EnterpriseRequestStatusKeys.Draft, StringComparison.Ordinal);

    private static Error InvalidStatus() => new(
        EnterpriseRequestWorkflowErrorCodes.InvalidStatus,
        "Only draft requests can be created, edited or deleted through ordinary writes.",
        ErrorType.Conflict);

    private static Error InvalidVersion() => new(
        EnterpriseRequestErrorCodes.VersionConflict,
        "The resource was updated concurrently.",
        ErrorType.Conflict);
}
