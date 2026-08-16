using System.Security.Cryptography;
using System.Text;
using Full.NET.Abstractions.Results;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Modules.CodeGeneration.Contracts;
using Full.NET.Modules.CodeGeneration.Features.NormalizeCrudSchema;

namespace Full.NET.Modules.CodeGeneration.Features.PreviewCrudGeneration;

/// <summary>
/// 通过共享归一化边界生成确定性的只读 CRUD 产物预览。
/// </summary>
internal sealed class CodeGenerationPreviewService(
    CodeGenerationSchemaNormalizer schemaNormalizer)
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    internal CodeGenerationPreviewService()
        : this(new CodeGenerationSchemaNormalizer())
    {
    }

    /// <summary>
    /// 在内存中生成预览；该流程不访问数据库，也不写入仓库或任务记录。
    /// </summary>
    /// <param name="request">管理端提交的显式生成输入。</param>
    /// <param name="cancellationToken">在开始生成前传播的取消信号。</param>
    /// <returns>成功时返回真实生成器产物，失败时返回稳定验证错误。</returns>
    public Result<CodeGenerationPreviewResponse> Preview(
        CodeGenerationPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var normalized = schemaNormalizer.Normalize(request);
        if (!normalized.IsSuccess)
        {
            return Result<CodeGenerationPreviewResponse>.Failure(
                normalized.Error!);
        }

        var schema = normalized.Value!.Schema;
        var artifacts = CrudArtifactGenerator
            .Generate(schema)
            .Select(artifact => new CodeGenerationPreviewArtifactResponse(
                artifact.RelativePath,
                ToMachineCode(artifact.Kind),
                ComputeSha256(artifact.Content),
                artifact.Content))
            .ToArray();

        return Result<CodeGenerationPreviewResponse>.Success(
            new CodeGenerationPreviewResponse(
                schema.DatabaseTableName,
                schema.ReadPermission,
                schema.WritePermission,
                Array.AsReadOnly(artifacts),
                schema.UsesLegacyEntityCapabilities
                    ? null
                    : schema.CreatePermission,
                schema.UsesLegacyEntityCapabilities
                    ? null
                    : schema.UpdatePermission,
                schema.UsesLegacyEntityCapabilities
                    ? null
                    : schema.DisablePermission));
    }

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
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "生成产物类型未配置稳定机器码。"),
        };

    private static string ComputeSha256(string content) =>
        Convert.ToHexString(SHA256.HashData(StrictUtf8.GetBytes(content)))
            .ToLowerInvariant();
}
