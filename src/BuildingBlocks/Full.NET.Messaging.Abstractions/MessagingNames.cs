using System.Text.RegularExpressions;

namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// 集成事件 Envelope 与 Metadata 的稳定长度、格式与内容类型约束。
/// </summary>
public static class MessagingNames
{
    /// <summary>
    /// MemoryPack 二进制序列化的 Content-Type 标识；当前 Envelope 仅支持此格式。
    /// </summary>
    public const string ContentTypeMemoryPack = "application/x-memorypack";

    /// <summary>
    /// Kafka PartitionKey 的 UTF-8 字节数上限；超过会导致 Broker 拒绝写入。
    /// </summary>
    public const int PartitionKeyMaxUtf8Bytes = 256;

    /// <summary>
    /// 关联 ID 字符串最大长度；超过会被契约校验拒绝。
    /// </summary>
    public const int CorrelationIdMaxLength = 128;

    /// <summary>
    /// 生产者模块标识的最大字符长度。
    /// </summary>
    public const int ProducerMaxLength = 128;

    /// <summary>
    /// 稳定事件类型名（四段式）的最大字符长度。
    /// </summary>
    public const int MessageTypeMaxLength = 256;

    /// <summary>
    /// W3C TraceParent 头部字符串的最大长度。
    /// </summary>
    public const int TraceParentMaxLength = 128;

    /// <summary>
    /// 逻辑 Topic 代码（三段式含版本后缀）的最大字符长度。
    /// </summary>
    public const int TopicCodeMaxLength = 128;

    /// <summary>
    /// Kafka Consumer Group 名称的最大字符长度。
    /// </summary>
    public const int ConsumerNameMaxLength = 128;

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

    /// <summary>
    /// 低基数 Topic 机器码，例如 tenancy.tenant-changed.v1。
    /// </summary>
    public static readonly Regex TopicCodePattern = new(
        "^[a-z][a-z0-9]*(?:\\.[a-z][a-z0-9_-]*)*\\.v[1-9][0-9]*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Kafka Consumer Group 稳定机器码，例如 fullnet.tenancy.projector。
    /// </summary>
    public static readonly Regex ConsumerNamePattern = new(
        "^[a-z][a-z0-9]*(?:\\.[a-z][a-z0-9_-]*)*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
}