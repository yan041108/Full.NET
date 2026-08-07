namespace Full.NET.Modules.Files.Contracts;

using Full.NET.Abstractions.Results;

/// <summary>引用 claim 的稳定消费者模块键。</summary>
public static class HostFileReferenceClaimConsumerModules
{
    public const string Document = "document";
}

/// <summary>引用 claim 状态机。</summary>
public static class HostFileReferenceClaimStates
{
    public const string Pending = "pending";
    public const string Active = "active";
    public const string Released = "released";
}

public static class HostFileReferenceClaimIdempotencyKeys
{
    public static string DocumentVersion(Guid versionId) => $"document-version:{versionId:D}";
}

public sealed record HostFileReferenceClaimRequest(
    string IdempotencyKey,
    string ConsumerModule,
    Guid ConsumerReferenceId,
    Guid FileId);

public sealed record HostFileReferenceClaimResult(
    Guid ClaimId,
    string State,
    HostFileReference FileReference);

public enum HostFileReferenceClaimProbeOutcome
{
    Exists = 0,
    NotFound = 1,
    Failed = 2,
}

public sealed record HostFileReferenceClaimProbeResult(HostFileReferenceClaimProbeOutcome Outcome);

public interface IHostFileReferenceClaimService
{
    Task<Result<HostFileReferenceClaimResult>> ClaimAsync(
        HostFileReferenceClaimRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<HostFileReferenceClaimResult>> ConfirmAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<Result<bool>> ReleaseAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<bool> HasOpenClaimsAsync(
        Guid fileId,
        CancellationToken cancellationToken = default);
}

/// <summary>消费方注册的精确引用探测，供 Files 对账超龄 Pending claim。</summary>
public interface IHostFileReferenceClaimProbe
{
    string ConsumerModule { get; }

    Task<HostFileReferenceClaimProbeResult> ProbeReferenceAsync(
        Guid consumerReferenceId,
        Guid fileId,
        CancellationToken cancellationToken = default);
}
