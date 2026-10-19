using System.Text.Json;
using Full.NET.Abstractions.Observability;
using Full.NET.Modules.ObservabilityAdmin.Features.ManageLogFiles;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.ObservabilityAdmin.Features.QueryLogTimeline;

/// <summary>复用文件控制面的防路径逃逸打开逻辑；只读至多十个文件、每个 256 KiB 尾部。</summary>
internal sealed class LocalFileTimelineReader(LogFileControlPlane files,
    IOptions<RuntimeLogTimelineOptions> options) : ILogTimelineReader
{
    public string Source => "runtime.local_file";
    public string RequiredPermission => "observability.log_files.read";

    public async Task<LogTimelineSourceResult> ReadAsync(LogTimelineQuery query, CancellationToken cancellationToken)
    {
        if (!options.Value.LocalFilesEnabled) return new([], "not_configured");
        var entries = new List<LogTimelineEntry>();
        foreach (var tail in await files.ReadTimelineTailsAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var line in tail.Content.Split('\n'))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (line.Length is 0 or > 65536) continue;
                try
                {
                    using var json = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 16 });
                    if (RuntimeLogTimelineParser.Parse(json.RootElement, Source, query) is { } entry) entries.Add(entry);
                }
                catch (JsonException) { /* 文件末尾可能正在写入，残缺行不能作为有效事件。 */ }
            }
        }
        // 尾读不能证明整个目录或请求区间完整，即使命中数为零也必须提示覆盖边界。
        return new(entries.DistinctBy(entry => entry.Id).OrderBy(entry => entry.OccurredAtUtc)
            .ThenBy(entry => entry.Id, StringComparer.Ordinal).Take(query.Limit).ToArray(), "bounded_scan");
    }
}
