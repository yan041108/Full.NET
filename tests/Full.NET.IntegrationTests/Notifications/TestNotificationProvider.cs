using System.Collections.Concurrent;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Providers;

namespace Full.NET.IntegrationTests.Notifications;

/// <summary>
/// 仅存在于测试程序集的 Provider。用于验证 Schema、租约 Worker、崩溃窗口和幂等键；不得编入生产发布物。
/// </summary>
internal sealed class TestNotificationProvider(TestNotificationProviderHarness harness)
    : INotificationProviderAdapter
{
    public const string ProviderTypeKeyValue = "test.notification";
    public const string ChannelKey = "test";
    public const string AdapterVersionValue = "1.0.0";
    public const string SecretFieldKey = "apiToken";

    public string? RecipientEndpointKindKey => null;

    public NotificationProviderTypeDescriptor Descriptor { get; } = new(
        ProviderTypeKeyValue,
        AdapterVersionValue,
        [ChannelKey],
        [
            new NotificationProviderConfigField("endpointBaseUrl", "string", true),
            new NotificationProviderConfigField("fromDisplayName", "string", false),
        ],
        [SecretFieldKey],
        SupportsNativeAot: true,
        ReceiptModeKey: "signed");

    public async ValueTask<NotificationProviderResult> SendAsync(
        NotificationProviderRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        harness.Record(request);
        switch (harness.Mode)
        {
            case TestNotificationProviderMode.Slow:
                await Task.Delay(harness.SlowDelay, cancellationToken).ConfigureAwait(false);
                return Succeed(request);
            case TestNotificationProviderMode.Crash:
                throw new InvalidOperationException("Test provider crashed after the lease was claimed.");
            case TestNotificationProviderMode.Transient:
                return new NotificationProviderResult(
                    false,
                    NotificationDeliveryRetry.Transient,
                    null,
                    null);
            case TestNotificationProviderMode.RateLimited:
                return new NotificationProviderResult(
                    false,
                    NotificationDeliveryRetry.RateLimited,
                    null,
                    harness.RetryAfter);
            case TestNotificationProviderMode.Permanent:
                return new NotificationProviderResult(
                    false,
                    NotificationDeliveryRetry.Permanent,
                    null,
                    null);
            default:
                return Succeed(request);
        }
    }

    private static NotificationProviderResult Succeed(NotificationProviderRequest request) =>
        new(
            true,
            NotificationDeliveryRetry.Succeeded,
            $"test-msg-{request.IdempotencyKey}",
            null);
}

/// <summary>要求使用受保护邮箱端点的测试 Provider，用于验证 Worker 不会回退到用户标识。</summary>
internal sealed class TestEndpointNotificationProvider(TestEndpointNotificationProviderHarness harness)
    : INotificationProviderAdapter
{
    public const string ProviderTypeKeyValue = "test.endpoint";
    public const string ChannelKey = "test-email";
    public const string EndpointKindKey = "email";

    public string? RecipientEndpointKindKey => EndpointKindKey;

    public NotificationProviderTypeDescriptor Descriptor { get; } = new(
        ProviderTypeKeyValue,
        "1.0.0",
        [ChannelKey],
        [new NotificationProviderConfigField("endpointBaseUrl", "string", true)],
        ["apiToken"],
        SupportsNativeAot: true,
        ReceiptModeKey: "none");

    public ValueTask<NotificationProviderResult> SendAsync(
        NotificationProviderRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        harness.Record(request);
        return ValueTask.FromResult(new NotificationProviderResult(
            true,
            NotificationDeliveryRetry.Succeeded,
            $"endpoint-msg-{request.IdempotencyKey}",
            null));
    }
}

/// <summary>记录 Worker 交给端点型 Provider 的完整请求；测试结束后不会持久化原始邮箱。</summary>
internal sealed class TestEndpointNotificationProviderHarness
{
    private readonly ConcurrentQueue<NotificationProviderRequest> _requests = new();

    public IReadOnlyCollection<NotificationProviderRequest> Requests => _requests.ToArray();

    public void Record(NotificationProviderRequest request) => _requests.Enqueue(request);

    public void Reset()
    {
        while (_requests.TryDequeue(out _))
        {
        }
    }
}

internal enum TestNotificationProviderMode
{
    Succeed,
    Transient,
    RateLimited,
    Permanent,
    Slow,
    Crash,
}

/// <summary>测试 Adapter 行为开关；默认成功且不自动发送，由集成测试显式调用 BatchProcessor。</summary>
internal sealed class TestNotificationProviderHarness
{
    private readonly ConcurrentBag<string> _idempotencyKeys = [];

    public TestNotificationProviderMode Mode { get; set; } = TestNotificationProviderMode.Succeed;

    public TimeSpan SlowDelay { get; set; } = TimeSpan.FromMilliseconds(800);

    public TimeSpan RetryAfter { get; set; } = TimeSpan.FromSeconds(30);

    public int SendCount => _idempotencyKeys.Count;

    public IReadOnlyCollection<string> IdempotencyKeys => _idempotencyKeys.ToArray();

    public void Record(NotificationProviderRequest request) =>
        _idempotencyKeys.Add(request.IdempotencyKey);

    public void Reset(TestNotificationProviderMode mode = TestNotificationProviderMode.Succeed)
    {
        Mode = mode;
        while (_idempotencyKeys.TryTake(out _))
        {
        }
    }
}
