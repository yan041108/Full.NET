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

    // 测试 Adapter 由 Harness 显式声明是否具备幂等重发能力，用于验证两种未知结果处置。
    public bool AllowsAutomaticRetryAfterUnknownResult => harness.AllowsAutomaticRetryAfterUnknownResult;

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
        using var activeSend = harness.EnterSend();
        harness.Record(request);
        if (harness.OnSendAsync is { } onSendAsync)
        {
            await onSendAsync(request, cancellationToken).ConfigureAwait(false);
        }
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
            case TestNotificationProviderMode.Unknown:
                return new NotificationProviderResult(
                    false,
                    NotificationDeliveryRetry.Unknown,
                    $"test-msg-{request.IdempotencyKey}",
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

    public bool AllowsAutomaticRetryAfterUnknownResult => false;

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
    Unknown,
    Slow,
    Crash,
}

/// <summary>测试 Adapter 行为开关；默认成功且不自动发送，由集成测试显式调用 BatchProcessor。</summary>
internal sealed class TestNotificationProviderHarness
{
    private readonly ConcurrentBag<string> _idempotencyKeys = [];
    private readonly ConcurrentBag<(Guid DeliveryId, string Key)> _keysByDelivery = [];
    private int _active;
    private int _peak;

    public int PeakConcurrency => Volatile.Read(ref _peak);

    public IDisposable EnterSend()
    {
        var active = Interlocked.Increment(ref _active);
        int peak;
        do { peak = Volatile.Read(ref _peak); if (peak >= active) break; }
        while (Interlocked.CompareExchange(ref _peak, active, peak) != peak);
        return new SendLease(this);
    }

    private sealed class SendLease(TestNotificationProviderHarness owner) : IDisposable
    {
        public void Dispose() => Interlocked.Decrement(ref owner._active);
    }

    public TestNotificationProviderMode Mode { get; set; } = TestNotificationProviderMode.Succeed;

    /// <summary>测试中显式声明“厂商可按幂等键去重”，用于覆盖结果未知时的自动重试分支。</summary>
    public bool AllowsAutomaticRetryAfterUnknownResult { get; set; }

    public TimeSpan SlowDelay { get; set; } = TimeSpan.FromMilliseconds(800);

    public TimeSpan RetryAfter { get; set; } = TimeSpan.FromSeconds(30);

    public int SendCount => _idempotencyKeys.Count;

    public IReadOnlyCollection<string> IdempotencyKeys => _idempotencyKeys.ToArray();

    /// <summary>按投递取已发送的幂等键，用于验证崩溃恢复与人工重试沿用同一键。</summary>
    public IReadOnlyCollection<string> IdempotencyKeysFor(Guid deliveryId) =>
        _keysByDelivery.Where(item => item.DeliveryId == deliveryId).Select(item => item.Key).ToArray();

    public Action? OnSend { get; set; }

    /// <summary>用受控信号保持厂商调用进行中，验证数据库事务不会随外发持续占用。</summary>
    public Func<NotificationProviderRequest, CancellationToken, Task>? OnSendAsync { get; set; }

    public void Record(NotificationProviderRequest request)
    {
        _idempotencyKeys.Add(request.IdempotencyKey);
        _keysByDelivery.Add((request.DeliveryId, request.IdempotencyKey));
        OnSend?.Invoke();
    }

    public void Reset(TestNotificationProviderMode mode = TestNotificationProviderMode.Succeed)
    {
        if (Volatile.Read(ref _active) != 0) throw new InvalidOperationException("不能重置仍在发送的测试提供程序。");
        Volatile.Write(ref _peak, 0);
        Mode = mode;
        OnSend = null;
        OnSendAsync = null;
        while (_idempotencyKeys.TryTake(out _))
        {
        }

        while (_keysByDelivery.TryTake(out _))
        {
        }
    }
}
