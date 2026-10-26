using Full.NET.Modules.Workflow.Domain;
using System.Text.Json.Serialization;

namespace Full.NET.Modules.Workflow.Features.ManageWorkCalendars;

/// <summary>创建当前可信作用域的工作日历，不接受租户或发布快照字段。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateWorkflowWorkCalendarRequest(string CalendarKey,string DisplayName,WorkflowWorkCalendarDraft Draft);
/// <summary>修改工作日历草稿，保持已发布版本不变。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateWorkflowWorkCalendarRequest(long ExpectedRevision,string DisplayName,WorkflowWorkCalendarDraft Draft);
/// <summary>以当前草稿修订发布固定工作窗口与时区规则。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PublishWorkflowWorkCalendarRequest(long ExpectedRevision);
/// <summary>日历列表的有界摘要。</summary>
public sealed record WorkflowWorkCalendarSummary(Guid Id,string CalendarKey,string DisplayName,long Revision,int? LatestVersionNumber,
    Guid? LatestVersionId,string? TimeZoneKey,DateTimeOffset CreatedAtUtc,DateTimeOffset UpdatedAtUtc);
/// <summary>当前可编辑草稿与最近发布版本引用。</summary>
public sealed record WorkflowWorkCalendarResponse(Guid Id,string CalendarKey,string DisplayName,WorkflowWorkCalendarDraft Draft,long Revision,
    int? LatestVersionNumber,Guid? LatestVersionId,DateTimeOffset CreatedAtUtc,DateTimeOffset UpdatedAtUtc);
/// <summary>不可变版本读取提供原工作窗口，内部固定时区规则不作为可编辑输入。</summary>
public sealed record WorkflowWorkCalendarVersionResponse(Guid Id,Guid CalendarId,int VersionNumber,WorkflowWorkCalendarDraft Calendar,
    string ContentHash,string TimeZoneKey,DateTimeOffset PublishedAtUtc);
/// <summary>已发布版本选择摘要。</summary>
public sealed record WorkflowWorkCalendarVersionSummary(Guid Id,Guid CalendarId,int VersionNumber,string ContentHash,string TimeZoneKey,DateTimeOffset PublishedAtUtc);
/// <summary>日历摘要按创建时间和标识稳定分页，每页最多20条。</summary>
public sealed record WorkflowWorkCalendarPageResponse(IReadOnlyList<WorkflowWorkCalendarSummary> Items,Guid? NextAfterId);
/// <summary>历史版本按单调版本号向前分页，每页最多20条。</summary>
public sealed record WorkflowWorkCalendarVersionPageResponse(IReadOnlyList<WorkflowWorkCalendarVersionSummary> Items,int? NextBeforeVersionNumber);
