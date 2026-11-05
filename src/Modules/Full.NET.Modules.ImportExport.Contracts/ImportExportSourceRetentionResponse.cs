namespace Full.NET.Modules.ImportExport.Contracts;

/// <summary>任务源工作簿的保留状态；不代表任务历史、业务记录或错误回执过期。</summary>
/// <param name="ExpiresAtUtc">创建任务时固定的来源截止时间。</param>
/// <param name="StateKey">稳定状态：available、retained_for_execution、expired、releasing 或 released。</param>
/// <param name="ReleasedAtUtc">实际源文件释放完成时间。</param>
public sealed record ImportExportSourceRetentionResponse(DateTimeOffset ExpiresAtUtc, string StateKey, DateTimeOffset? ReleasedAtUtc);
