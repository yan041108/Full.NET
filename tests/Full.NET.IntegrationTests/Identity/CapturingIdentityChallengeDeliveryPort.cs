using Full.NET.Modules.Identity.Contracts;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Notifications.Contracts;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>集成测试辅助：CapturingIdentityChallengeDeliveryPort。</summary>
internal sealed class CapturingIdentityChallengeDeliveryPort : IIdentityChallengeDeliveryPort
{
    private readonly object _gate = new();
    private IdentityChallengeDeliveryIntent? _lastIntent;

    public IdentityChallengeDeliveryIntent? LastIntent
    {
        get
        {
            lock (_gate)
            {
                return _lastIntent;
            }
        }
    }

    public Task<Result<bool>> SendAsync(
        IdentityChallengeDeliveryIntent intent,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _lastIntent = intent;
        }

        return Task.FromResult(Result<bool>.Success(true));
    }
}
