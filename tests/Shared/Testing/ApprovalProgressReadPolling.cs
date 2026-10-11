using System.Net;
using System.Text.Json;

namespace Full.NET.Testing;

/// <summary>只识别审批进度的可重试读取快照冲突，其他响应仍由 HTTP 断言处理。</summary>
internal static class ApprovalProgressReadPolling
{
    internal static bool IsSnapshotConflict(string path, HttpStatusCode statusCode, string body)
    {
        const string prefix = "/api/v1/enterprise_request/enterprise-requests/";
        const string suffix = "/approval-progress";
        if (statusCode != HttpStatusCode.Conflict || !path.StartsWith(prefix, StringComparison.Ordinal) ||
            !path.EndsWith(suffix, StringComparison.Ordinal) || path.Length <= prefix.Length + suffix.Length ||
            !Guid.TryParseExact(path.AsSpan(prefix.Length, path.Length - prefix.Length - suffix.Length), "D", out var id) || id == Guid.Empty)
            return false;
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String &&
                code.GetString() == "enterprise_request.enterprise_requests.version_conflict";
        }
        catch (JsonException)
        {
            // 非标准错误必须交给原 HTTP 断言，不能作为可重试快照吞掉。
            return false;
        }
    }
}
