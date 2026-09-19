namespace Full.NET.Modules.Webhooks.Delivery;

internal sealed class WebhookDeliveryWorkerOptions
{
    public const string SectionName = "Webhooks:Delivery";

    public int PollMilliseconds { get; set; } = 1000;

    public int BatchSize { get; set; } = 10;

    public int MaxAttempts { get; set; } = 5;

    public int TimeoutSeconds { get; set; } = 30;
}
