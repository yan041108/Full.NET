using System.Text.Json.Serialization;

namespace Full.NET.Modules.Ai.Contracts;

/// <summary>显式选择本人日程，只请求当前来源预览，不进行模型派发或业务操作。</summary>
/// <param name="ScheduleIds">最多八个非空且唯一的本人日程标识。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PreviewAiPersonalScheduleSources(IReadOnlyList<Guid> ScheduleIds);

/// <summary>当前会话已核证的固定字段；客户端不能将此展示数据再提交为授权证明。</summary>
/// <param name="Kind">固定来源协议。</param>
/// <param name="Purpose">固定字段用途。</param>
/// <param name="Id">日程标识。</param>
/// <param name="Version">准确业务版本。</param>
/// <param name="ProjectionHash">服务器规范字段摘要。</param>
/// <param name="Content">本人日程内容。</param>
/// <param name="StartAtUtc">UTC 计划开始时间。</param>
/// <param name="EndAtUtc">UTC 计划结束时间。</param>
/// <param name="Status">当前状态。</param>
public sealed record AiPersonalScheduleSource(string Kind, string Purpose, Guid Id, int Version,
    string ProjectionHash, string Content, DateTimeOffset StartAtUtc, DateTimeOffset EndAtUtc, string Status);

/// <summary>仅本次可读的完整批次，无模型费用、批准或完成状态。</summary>
/// <param name="Classification">固定 restricted 下限。</param>
/// <param name="Sources">全部选择来源，任何一个不可读时不交付部分内容。</param>
public sealed record AiPersonalScheduleSourcePreview(string Classification, IReadOnlyList<AiPersonalScheduleSource> Sources);

/// <summary>来源预览的期望身份；不是授权凭证，问答前必须重新读取服务器权威字段。</summary>
/// <param name="Id">已选择的本人日程。</param>
/// <param name="Version">预览时准确业务版本。</param>
/// <param name="ProjectionHash">预览时固定字段摘要。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiPersonalScheduleSourceSelection(Guid Id, int Version, string ProjectionHash);

/// <summary>仅显示已核证的业务引用身份，绝不借用文档或文件标识。</summary>
/// <param name="Id">服务器分配的引用身份。</param>
/// <param name="Kind">固定日程来源种类。</param>
/// <param name="Purpose">固定字段用途。</param>
/// <param name="ScheduleId">真实日程标识。</param>
/// <param name="Version">实际输入版本。</param>
/// <param name="ProjectionHash">实际输入的固定字段摘要。</param>
public sealed record AiPersonalScheduleCitationResponse(Guid Id, string Kind, string Purpose,
    Guid ScheduleId, int Version, string ProjectionHash);
