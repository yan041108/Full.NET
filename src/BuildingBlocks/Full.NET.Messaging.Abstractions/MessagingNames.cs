using System.Text.RegularExpressions;

namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// 集成事件 Envelope 与 Metadata 的稳定长度、格式与内容类型约束。
/// </summary>
public static class MessagingNames
{
    public const string ContentTypeMessagePack = "application/x-msgpack";

    public const int PartitionKeyMaxUtf8Bytes = 256;

    public const int CorrelationIdMaxLength = 128;

    public const int ProducerMaxLength = 128;

    public const int MessageTypeMaxLength = 256;

    public const int TraceParentMaxLength = 128;

    /// <summary>
    /// 规范消息类型：至少四段 owner.module.entity.event。
    /// </summary>
    public static readonly Regex MessageTypePattern = new(
        "^[a-z][a-z0-9]*(\\.[a-z][a-z0-9_]*){3,}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// 稳定生产者机器码，例如 fullnet.tenancy。
    /// </summary>
    public static readonly Regex ProducerPattern = new(
        "^[a-z][a-z0-9]*(?:\\.[a-z][a-z0-9_]*)*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// W3C Trace Context traceparent 头格式。
    /// </summary>
    public static readonly Regex TraceParentPattern = new(
        "^00-[0-9a-f]{32}-[0-9a-f]{16}-[0-9a-f]{2}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);
}