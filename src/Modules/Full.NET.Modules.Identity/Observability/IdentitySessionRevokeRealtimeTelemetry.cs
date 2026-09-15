using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Full.NET.Modules.Identity.Observability;

/// <summary>记录 OIDC/会话撤销实时通知发布结果；旁路指标，失败不得影响权威撤销语义。</summary>
internal static class IdentitySessionRevokeRealtimeTelemetry
{
    public const string MeterName = "fullnet.identity";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> PublishAttempts =
        Meter.CreateCounter<long>(
            "fullnet.identity.session_revoke.realtime.publish.attempts",
            unit: "{attempt}");

    public static void RecordAttempt(string outcome)
    {
        try
        {
            var tags = new TagList
            {
                { "outcome", outcome },
            };
            PublishAttempts.Add(1, tags);
        }
        catch (Exception)
        {
            // 指标消费者属于旁路；其失败不得改变撤销投递结果。
        }
    }
}