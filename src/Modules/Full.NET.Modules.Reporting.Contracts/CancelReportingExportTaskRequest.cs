using System.Text.Json.Serialization;

namespace Full.NET.Modules.Reporting.Contracts;

/// <summary>以最近读取的任务版本停止本人排队工作，不能覆盖正在生成的版本。</summary>
/// <param name="Version">任务并发版本，不是报表发布版本号。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CancelReportingExportTaskRequest([property: JsonRequired] long Version);

/// <summary>本人待执行报表取消的稳定错误码。</summary>
public static class ReportingExportCancellationErrorCodes
{
    /// <summary>版本已改变或任务已经被领取，必须刷新后再判断操作资格。</summary>
    public const string Conflict = "reporting.export_task.cancel_conflict";
}
