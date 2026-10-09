using System.Text;
using System.Text.Json;

namespace Full.NET.UnitTests.Ai.Evaluation;

/// <summary>有界读取离线观测，报告只新建不覆盖，保留可审查的原始证据。</summary>
internal static class RagRetrievalComparisonArtifacts
{
    private const int MaximumEvidenceBytes = 8 * 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static RagRetrievalComparisonInput Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var length = stream.Length;
        if (length is <= 0 or > MaximumEvidenceBytes) throw new InvalidDataException("比较证据必须为非空且不超过 8 MiB 的 UTF-8 JSON。");
        var bytes = new byte[checked((int)length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new InvalidDataException("比较证据在读取期间发生变化。");
        // 只接受 UTF-8，可兼容 Windows 写入的 UTF-8 BOM，不能自动改读 UTF-16。
        var offset = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
        return RagEvaluationJson.Read<RagRetrievalComparisonInput>(Utf8.GetString(bytes, offset, bytes.Length - offset));
    }

    internal static void Write(string path, RagRetrievalComparisonReport report)
    {
        var bytes = Utf8.GetBytes(JsonSerializer.Serialize(report, RagEvaluationJson.Options) + "\n");
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        // CreateNew 在实际打开时拒绝既有路径，避免先检查后覆盖的竞态，也保护原始输入。
        using var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
        stream.Flush(true);
    }
}
