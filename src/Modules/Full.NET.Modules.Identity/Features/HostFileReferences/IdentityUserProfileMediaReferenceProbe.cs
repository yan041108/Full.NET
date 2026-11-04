using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Features.HostFileReferences;

/// <summary>为 Files claim 对账提供 Identity 用户资料媒体精确引用探测。</summary>
internal sealed class IdentityUserProfileMediaReferenceProbe(IQueryExecutor queryExecutor)
    : IHostImmutableFileBindingClaimProbe
{
    public string ConsumerModule => HostFileReferenceClaimConsumerModules.Identity;

    /// <summary>按主体批量读取当前绑定；旧键和缺失键不构成退休证据，一次性旧代际才可补偿。</summary>
    public async Task<IReadOnlyDictionary<Guid, HostFileReferenceClaimProbeResult>> ProbeBindingsAsync(
        IReadOnlyList<HostFileBindingClaimProbeRequest> requests, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests); cancellationToken.ThrowIfCancellationRequested();
        if (requests.Count > 1000) throw new ArgumentOutOfRangeException(nameof(requests));
        var results = requests.Select(r => r.ClaimId).Distinct().ToDictionary(id => id,
            _ => new HostFileReferenceClaimProbeResult(HostFileReferenceClaimProbeOutcome.Failed));
        if (results.Count != requests.Count || requests.Any(r => r.ClaimId == Guid.Empty || r.ConsumerReferenceId == Guid.Empty || r.FileId == Guid.Empty)) return results;
        var valid = requests.Where(r => IsGeneration(r, true) || IsGeneration(r, false)).ToArray();
        if (valid.Length == 0) return results;
        try
        {
            var profiles = (await queryExecutor.QueryAsync<HostUserProfileRecord>(IdentitySql.ListHostUserProfilesByIds,
                IdentitySqlParameters.Create(("UserIds", valid.Select(r => r.ConsumerReferenceId).Distinct().ToArray())), cancellationToken)
                .ConfigureAwait(false)).ToDictionary(p => p.UserId);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var request in valid)
            {
                if (!profiles.TryGetValue(request.ConsumerReferenceId, out var profile))
                { results[request.ClaimId] = new(HostFileReferenceClaimProbeOutcome.NotFound); continue; }
                var avatar = IsGeneration(request, true);
                results[request.ClaimId] = new(ActiveOutcome(request, avatar,
                    avatar ? profile.AvatarFileId : profile.SignatureFileId, avatar ? profile.AvatarClaimKey : profile.SignatureClaimKey));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            // 整批失败时撤回任何局部回答，不能把不完整权威读取当作永久退休。
            foreach (var id in results.Keys.ToArray()) results[id] = new(HostFileReferenceClaimProbeOutcome.Failed);
        }
        return results;
    }

    private static bool IsGeneration(HostFileBindingClaimProbeRequest request, bool avatar) =>
        HostFileReferenceClaimIdempotencyKeys.IsGenerationBindingKey(avatar
            ? HostFileReferenceClaimIdempotencyKeys.IdentityUserAvatar(request.ConsumerReferenceId, request.FileId)
            : HostFileReferenceClaimIdempotencyKeys.IdentityUserSignature(request.ConsumerReferenceId, request.FileId), request.IdempotencyKey);

    private static HostFileReferenceClaimProbeOutcome ActiveOutcome(HostFileBindingClaimProbeRequest request, bool avatar,
        Guid? currentFile, string? storedKey)
    {
        if (currentFile is null) return string.IsNullOrWhiteSpace(storedKey) ? HostFileReferenceClaimProbeOutcome.NotFound : HostFileReferenceClaimProbeOutcome.Failed;
        if (currentFile == Guid.Empty) return HostFileReferenceClaimProbeOutcome.Failed;
        var legacy = avatar ? HostFileReferenceClaimIdempotencyKeys.IdentityUserAvatar(request.ConsumerReferenceId, currentFile.Value)
            : HostFileReferenceClaimIdempotencyKeys.IdentityUserSignature(request.ConsumerReferenceId, currentFile.Value);
        if (!string.IsNullOrWhiteSpace(storedKey) && !HostFileReferenceClaimIdempotencyKeys.IsBindingKey(legacy, storedKey)) return HostFileReferenceClaimProbeOutcome.Failed;
        if (currentFile != request.FileId) return HostFileReferenceClaimProbeOutcome.NotFound;
        // 当前仍占用同文件却没有可证明的代际时，保留而不是猜测该扫描键已退休。
        if (!HostFileReferenceClaimIdempotencyKeys.IsGenerationBindingKey(legacy, storedKey!)) return HostFileReferenceClaimProbeOutcome.Failed;
        return string.Equals(storedKey, request.IdempotencyKey, StringComparison.Ordinal) ? HostFileReferenceClaimProbeOutcome.Exists : HostFileReferenceClaimProbeOutcome.NotFound;
    }

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
