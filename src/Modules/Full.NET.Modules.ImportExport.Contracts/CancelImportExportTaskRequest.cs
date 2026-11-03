using System.Text.Json.Serialization;

namespace Full.NET.Modules.ImportExport.Contracts;

/// <summary>取消只能依据最近读取的详情版本，不能覆盖 Worker 已经推进的状态。</summary>
/// <param name="Version">最近读取的任务正整数版本。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CancelImportExportTaskRequest([property: JsonRequired] long Version);

/// <summary>本人任务取消的稳定错误码。</summary>
public static class ImportTaskCancellationErrorCodes
{
    /// <summary>任务版本或状态已改变，或当前批次已被 Worker 领取。</summary>
    public const string Conflict = "import_export.task.cancel_conflict";
}
