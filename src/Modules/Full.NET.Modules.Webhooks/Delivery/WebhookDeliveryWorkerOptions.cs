/// <summary>模块运行期选项；由配置节绑定并在启动期校验。</summary>
namespace Full.NET.Modules.Webhooks.Delivery;

internal sealed class WebhookDeliveryWorkerOptions
{
    public const string SectionName = "Webhooks:Delivery";

    public int PollMilliseconds { get; set; } = 1000;

    public int BatchSize { get; set; } = 10;

    public int MaxAttempts { get; set; } = 5;

    /// <summary>领取后允许占用的秒数；外发超时必须小于租约。</summary>
    public int LeaseSeconds { get; set; } = 60;

    public int TimeoutSeconds { get; set; } = 30;
}
