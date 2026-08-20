using System.Security.Cryptography;
using System.Text;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Modules.CodeGeneration.Contracts;

namespace Full.NET.Modules.CodeGeneration.Features.ManageHostRuns;

/// <summary>
/// 统一计算运行摘要，使预览确认与 Apply 复核使用完全相同的确定性输入。
/// </summary>
internal static class CodeGenerationRunSummary
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static string ComputeManifestSha256(
        IReadOnlyList<GeneratedArtifact> artifacts)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        var manifest = string.Concat(artifacts
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .Select(artifact =>
                $"{artifact.RelativePath}\n{ToMachineCode(artifact.Kind)}\n"
                + $"{ComputeContentSha256(artifact.Content)}\n"));
        return Convert.ToHexString(
                SHA256.HashData(StrictUtf8.GetBytes(manifest)))
            .ToLowerInvariant();
    }

    public static string ComputeManifestSha256(
        IReadOnlyList<CodeGenerationPreviewArtifactResponse> artifacts)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        var manifest = string.Concat(artifacts
            .OrderBy(artifact => artifact.Path, StringComparer.Ordinal)
            .Select(artifact =>
                $"{artifact.Path}\n{artifact.Kind}\n{artifact.Sha256}\n"));
        return Convert.ToHexString(
                SHA256.HashData(StrictUtf8.GetBytes(manifest)))
            .ToLowerInvariant();
    }

    /// <summary>
    /// 计算回滚后工作区 GenerationManifest 的稳定摘要；空清单合法。
    /// </summary>
    public static string ComputeManifestSha256(GenerationManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return Convert.ToHexString(
                SHA256.HashData(StrictUtf8.GetBytes(manifest.ToJson())))
            .ToLowerInvariant();
    }

    private static string ComputeContentSha256(string content) =>
        Convert.ToHexString(
                SHA256.HashData(StrictUtf8.GetBytes(content)))
            .ToLowerInvariant();

    private static string ToMachineCode(GeneratedArtifactKind kind) =>
        kind switch
        {
            GeneratedArtifactKind.Backend => "backend",
            GeneratedArtifactKind.VueClient => "vue_client",
            GeneratedArtifactKind.LayuiClient => "layui_client",
            GeneratedArtifactKind.Report => "report",
            GeneratedArtifactKind.MigrationTemplate => "migration_template",
            GeneratedArtifactKind.IntegrationTestTemplate =>
                "integration_test_template",
            GeneratedArtifactKind.VueView => "vue_view",
            GeneratedArtifactKind.OpenApiContract => "openapi_contract",
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "生成产物类型未配置稳定机器码。"),
        };
}
