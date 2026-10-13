using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Features.HostFileReferences;

/// <summary>为 Files claim 对账提供 Identity 用户资料媒体精确引用探测。</summary>
internal sealed class IdentityUserProfileMediaReferenceProbe(IQueryExecutor queryExecutor)
    : IHostFileReferenceBindingClaimProbe
{
    public string ConsumerModule => HostFileReferenceClaimConsumerModules.Identity;

    public async Task<HostFileReferenceClaimProbeResult> ProbeBindingAsync(Guid consumerReferenceId, Guid fileId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (consumerReferenceId == Guid.Empty || fileId == Guid.Empty || string.IsNullOrWhiteSpace(idempotencyKey))
            return new(HostFileReferenceClaimProbeOutcome.Failed);
        try
        {
            // 一次权威读取同时核对文件及实际代际，不能用另一种媒体或另一代际确认失败绑定。
            var profile = (await queryExecutor.QueryAsync<HostUserProfileRecord>(IdentitySql.ListHostUserProfilesByIds,
                IdentitySqlParameters.Create(("UserIds", new[] { consumerReferenceId })), cancellationToken).ConfigureAwait(false)).FirstOrDefault();
            cancellationToken.ThrowIfCancellationRequested();
            var exists = profile is not null && (
                profile.AvatarFileId == fileId && Matches(HostFileReferenceClaimIdempotencyKeys.IdentityUserAvatar(consumerReferenceId, fileId), profile.AvatarClaimKey, idempotencyKey)
                || profile.SignatureFileId == fileId && Matches(HostFileReferenceClaimIdempotencyKeys.IdentityUserSignature(consumerReferenceId, fileId), profile.SignatureClaimKey, idempotencyKey));
            return new(exists ? HostFileReferenceClaimProbeOutcome.Exists : HostFileReferenceClaimProbeOutcome.NotFound);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return new(HostFileReferenceClaimProbeOutcome.Failed); }
    }

    private static bool Matches(string legacy, string? stored, string scanned)
    {
        // 与媒体写入相同的历史回退规则：跨文件旧键或空字段不代表当前代际。
        var effective = stored is not null && HostFileReferenceClaimIdempotencyKeys.IsBindingKey(legacy, stored) ? stored : legacy;
        return string.Equals(effective, scanned, StringComparison.Ordinal);
    }

    public async Task<HostFileReferenceClaimProbeResult> ProbeReferenceAsync(
        Guid consumerReferenceId,
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var exists = await queryExecutor.QuerySingleOrDefaultAsync<int>(
                    IdentitySql.HostUserProfileMediaExists,
                    IdentitySqlParameters.Create(
                        ("UserId", consumerReferenceId),
                        ("FileId", fileId),
                        ("Kind", "avatar")),
                    cancellationToken)
                .ConfigureAwait(false);
            if (exists == 1)
            {
                return new HostFileReferenceClaimProbeResult(
                    HostFileReferenceClaimProbeOutcome.Exists);
            }

            exists = await queryExecutor.QuerySingleOrDefaultAsync<int>(
                    IdentitySql.HostUserProfileMediaExists,
                    IdentitySqlParameters.Create(
                        ("UserId", consumerReferenceId),
                        ("FileId", fileId),
                        ("Kind", "signature")),
                    cancellationToken)
                .ConfigureAwait(false);
            return new HostFileReferenceClaimProbeResult(
                exists == 1
                    ? HostFileReferenceClaimProbeOutcome.Exists
                    : HostFileReferenceClaimProbeOutcome.NotFound);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new HostFileReferenceClaimProbeResult(
                HostFileReferenceClaimProbeOutcome.Failed);
        }
    }
}
