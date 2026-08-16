using System.IO.Compression;
using System.Text;
using Full.NET.Data.CodeGeneration.Generation;

namespace Full.NET.Data.CodeGeneration.Packaging;

/// <summary>
/// 把已成功 Preview/Apply 的产物打成确定性 zip，禁止依赖本机时间或路径分隔符。
/// </summary>
public static class GeneratedArtifactZip
{
    private static readonly UTF8Encoding Utf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly DateTimeOffset ZipEpoch =
        new(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>按正斜杠路径排序后写入；条目使用 LF 文本与固定时间戳。</summary>
    public static byte[] Create(IReadOnlyList<GeneratedArtifact> artifacts)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var artifact in artifacts
                         .OrderBy(
                             item => item.RelativePath,
                             StringComparer.Ordinal))
            {
                var entry = zip.CreateEntry(
                    artifact.RelativePath.Replace('\\', '/'),
                    CompressionLevel.NoCompression);
                entry.LastWriteTime = ZipEpoch;
                using var writer = new StreamWriter(entry.Open(), Utf8);
                writer.NewLine = "\n";
                writer.Write(artifact.Content);
            }
        }

        return stream.ToArray();
    }
}
