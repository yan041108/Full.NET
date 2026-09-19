using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Features.ManageRegistrationPolicy;

/// <summary>注册策略单例读取与更新。</summary>
internal sealed class RegistrationPolicyService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock)
{
    /// <summary>读取当前注册策略。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>注册策略或稳定业务错误。</returns>
    public async Task<Result<RegistrationPolicyResponse>> GetAsync(
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<RegistrationPolicyRecord>(
                RegistrationPolicySql.GetPolicy,
                IdentitySqlParameters.Create(
                    ("PolicyId", IdentityRegistrationPolicyConstants.PolicyId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return Result<RegistrationPolicyResponse>.Failure(new Error(
                IdentityErrorCodes.RegistrationPolicyNotFound,
                "The registration policy was not found.",
                ErrorType.NotFound));
        }

        return Result<RegistrationPolicyResponse>.Success(Map(record));
    }

    /// <summary>更新注册策略。</summary>
    /// <param name="request">更新请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>更新后的策略或稳定业务错误。</returns>
    public async Task<Result<RegistrationPolicyResponse>> UpdateAsync(
        UpdateRegistrationPolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        var mode = ResolveRegistrationMode(request);
        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                RegistrationPolicySql.UpdatePolicy,
                IdentitySqlParameters.Create(
                    ("PolicyId", IdentityRegistrationPolicyConstants.PolicyId),
                    ("IsPublicRegistrationEnabled", mode == IdentityRegistrationMode.Open),
                    ("RegistrationMode", (byte)mode),
                    ("UpdatedAtUtc", now),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows < 1)
        {
            return Result<RegistrationPolicyResponse>.Failure(new Error(
                IdentityErrorCodes.RegistrationPolicyVersionConflict,
                "The registration policy was modified by another request.",
                ErrorType.Conflict));
        }

        return await GetAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static RegistrationPolicyResponse Map(RegistrationPolicyRecord record)
    {
        var mode = record.RegistrationMode == 0
            ? (record.IsPublicRegistrationEnabled
                ? IdentityRegistrationMode.Open
                : IdentityRegistrationMode.InvitationOnly)
            : (IdentityRegistrationMode)record.RegistrationMode;
        return new(
            record.Id,
            mode == IdentityRegistrationMode.Open,
            mode,
            record.UpdatedAtUtc,
            record.Version);
    }

    internal static IdentityRegistrationMode ResolveRegistrationMode(UpdateRegistrationPolicyRequest request) =>
        request.RegistrationMode
        ?? (request.IsPublicRegistrationEnabled
            ? IdentityRegistrationMode.Open
            : IdentityRegistrationMode.InvitationOnly);
}
