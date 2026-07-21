using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Security;
using IdentityUser = Full.NET.Modules.Identity.Domain.IdentityUser;

namespace Full.NET.UnitTests.Identity;

/// <summary>测试替身：避免对 internal 接口做 NSubstitute 代理。</summary>
internal sealed class StubStrongReauthenticationProvider(
    bool isProductionEligible,
    Result<IdentityUser> verifyResult) : IStrongReauthenticationProvider
{
    public bool IsProductionEligible { get; } = isProductionEligible;

    public Task<Result<IdentityUser>> VerifyAsync(
        Guid operatorUserId,
        string currentPassword,
        string? totpCode,
        CancellationToken cancellationToken = default)
    {
        _ = operatorUserId;
        _ = currentPassword;
        _ = totpCode;
        _ = cancellationToken;
        return Task.FromResult(verifyResult);
    }
}
