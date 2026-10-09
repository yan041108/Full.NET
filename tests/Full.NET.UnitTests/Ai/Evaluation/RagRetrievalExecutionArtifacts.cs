using System.Text;
using System.Text.Json;

namespace Full.NET.UnitTests.Ai.Evaluation;

/// <summary>严格有界读取外部向量及分数；报告新建且不覆盖输入，不输出任何候选正文。</summary>
internal static class RagRetrievalExecutionArtifacts
{
    private const int MaximumBytes = 8 * 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    internal static RagRetrievalExecutionInput Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 0 or > MaximumBytes) throw new InvalidDataException("invalid_retrieval_execution_file_size");
        var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new InvalidDataException("retrieval_execution_file_changed");
        var offset = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
        return RagEvaluationJson.Read<RagRetrievalExecutionInput>(Utf8.GetString(bytes, offset, bytes.Length - offset));
    }

    internal static void Write(string path, RagRetrievalExecutionReport report, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var bytes = Utf8.GetBytes(JsonSerializer.Serialize(report, RagEvaluationJson.Options) + "\n");
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("retrieval_execution_report_budget");
        var full = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        // 创建输出前复核取消；一旦开始写入则完成当前同步写入，不留下主动取消导致的半份证据。
        token.ThrowIfCancellationRequested();
        using var stream = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes); stream.Flush(true);
    }
}
