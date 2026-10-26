namespace Full.NET.Modules.Messaging.Contracts;

/// <summary>为受控流开始一代采集；请求键和预期代次共同保护网络重试，不接受载荷或资源地址。</summary>
/// <param name="RequestId">同一创建意图内保持不变的客户端 UUID。</param>
/// <param name="ExpectedGeneration">已读取的上一代；初次采集为空。旧意图不能覆盖更新代次。</param>
/// <param name="EventType">服务端策略允许且已登记的事件流。</param>
/// <param name="SchemaVersion">已登记版本。</param>
/// <param name="MaxEvents">本代数量上限，不超过部署策略。</param>
/// <param name="WindowSeconds">本代固定期限，不超过部署策略。</param>
/// <param name="Reason">可信操作者提供的人工理由。</param>
public sealed record StartShadowCaptureRequest(Guid RequestId, Guid? ExpectedGeneration, string EventType, int SchemaVersion,
    int MaxEvents, int WindowSeconds, string Reason);

/// <summary>关闭指定采集代次的人工理由。</summary>
/// <param name="Reason">操作理由；不允许通过请求改变目标代次或操作者身份。</param>
public sealed record CloseShadowCaptureRequest(string Reason);

/// <summary>影子采集元数据；不向浏览器返回任何业务载荷或内部资源。</summary>
/// <param name="Generation">当前采集代次的服务端 UUID v7。</param>
/// <param name="EventType">已登记的业务事件流。</param>
/// <param name="SchemaVersion">事件版本。</param>
/// <param name="State">active、closed、blocked 或仅查询派生的 expired；关闭不代表证明已建立。</param>
/// <param name="MaxEvents">本代固定数量预算。</param>
/// <param name="MaxPayloadBytes">本代固定载荷字节预算。</param>
/// <param name="CapturedCount">与 Legacy 同事务确认的采集数量。</param>
/// <param name="CapturedBytes">已确认的真实载荷字节总数。</param>
/// <param name="MissingMetadataCount">本代缺少元数据的写入次数；非零时禁止建立证明。</param>
/// <param name="StartedAtUtc">采集开始时间。</param>
/// <param name="ExpiresAtUtc">不可自动延长的截止时间。</param>
/// <param name="ClosedAtUtc">持久关闭或阻断时间；派生过期仍可能为空。</param>
/// <param name="EndReason">关闭或阻断原因码，不作为链路就绪证明。</param>
public sealed record ShadowCaptureResponse(Guid Generation, string EventType, int SchemaVersion, string State,
    int MaxEvents, long MaxPayloadBytes, int CapturedCount, long CapturedBytes, int MissingMetadataCount,
    DateTimeOffset StartedAtUtc, DateTimeOffset ExpiresAtUtc, DateTimeOffset? ClosedAtUtc, string? EndReason);

/// <summary>只读部署策略；关闭时所有流和预算字段为空。</summary>
/// <param name="Enabled">部署是否允许受控影子采集。</param>
/// <param name="EventType">唯一允许采集的事件类型。</param>
/// <param name="SchemaVersion">唯一允许的事件版本。</param>
/// <param name="MaximumEvents">每代数量上限。</param>
/// <param name="MaximumPayloadBytes">每代载荷字节上限。</param>
/// <param name="MaximumWindowSeconds">每代最长采集秒数。</param>
public sealed record ShadowCapturePolicyResponse(bool Enabled, string? EventType, int? SchemaVersion,
    int? MaximumEvents, long? MaximumPayloadBytes, int? MaximumWindowSeconds);
