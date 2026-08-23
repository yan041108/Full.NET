namespace Full.NET.Realtime;

/// <summary>
/// 实时下行消息信封：稳定机器码 + 可选结构化数据，禁止依赖翻译文本。
/// </summary>
/// <remarks>
/// <para><see cref="Code"/> 属于稳定机器契约，不可本地化、不可语义化、不可包含运行时拼接出的字符串；
/// 调用方按已登记常量（如 <see cref="RealtimeMessageCodes"/>）发送，消费方按 Code 选择客户端本地资源或处理逻辑。</para>
/// <para><see cref="Data"/> 仅承载与该 Code 配套的结构化载荷，键值对不应包含闭包、委托或不可序列化的领域实体；
/// 类型为只读字典可空，缺省时表示该消息无附加数据。载荷应在生产端剪裁到客户端必需字段，
/// 不得用实时通道传输大尺寸或敏感数据。</para>
/// </remarks>
/// <param name="Code">稳定消息机器码，匹配已登记常量集合；客户端依赖该码做行为分支。</param>
/// <param name="Data">可选结构化载荷；为 <c>null</c> 表示无附加数据，非空时键名应保持稳定契约风格。</param>
public sealed record RealtimeMessage(
    string Code,
    IReadOnlyDictionary<string, object?>? Data = null);
