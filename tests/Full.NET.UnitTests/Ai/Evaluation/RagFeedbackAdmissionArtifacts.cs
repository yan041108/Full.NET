using System.Text;
using System.Text.Json;

namespace Full.NET.UnitTests.Ai.Evaluation;

/// <summary>整个候选包有界且严格读取；单文件封装语料及回执，避免只发布其中一半。</summary>
internal static class RagFeedbackAdmissionArtifacts
{
    private const int MaximumBytes = 2 * 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    internal static RagFeedbackAdmissionInput ReadInput(string path) => Read<RagFeedbackAdmissionInput>(path);
    internal static (RagEvaluationCorpus Corpus, string Digest) ReadCorpus(string path)
        => RagFeedbackAdmission.Verify(Read<RagFeedbackAdmissionArtifact>(path));
    internal static RagEvaluationReport Score(string path, RagEvaluationRun run)
        => RagFeedbackAdmission.Score(Read<RagFeedbackAdmissionArtifact>(path), run);

    private static T Read<T>(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 0 or > MaximumBytes) throw new InvalidDataException("invalid_feedback_admission_file_size");
        var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new InvalidDataException("feedback_admission_file_changed");
        var offset = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
        return RagEvaluationJson.Read<T>(Utf8.GetString(bytes, offset, bytes.Length - offset));
    }

    internal static void Write(string path, RagFeedbackAdmissionArtifact artifact)
    {
        RagFeedbackAdmission.Verify(artifact);
        WriteNew(path, artifact);
    }

    internal static void WriteReport(string path, RagEvaluationReport report) => WriteNew(path, report);

    private static void WriteNew<T>(string path, T value)
    {
        var bytes = Utf8.GetBytes(JsonSerializer.Serialize(value, RagEvaluationJson.Options) + "\n");
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("feedback_admission_output_budget");
        var full = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        // 实际创建时拒绝既有文件与路径别名，既不覆盖旧基线，也不覆盖候选输入或评估证据。
        using var stream = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes); stream.Flush(true);
    }
}
