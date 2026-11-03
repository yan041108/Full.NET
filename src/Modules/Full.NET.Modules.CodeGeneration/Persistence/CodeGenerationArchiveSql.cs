using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.CodeGeneration.Persistence;

/// <summary>归档只访问本模块元数据，不依赖模板、工作区或其他模块的文件表。</summary>
internal static class CodeGenerationArchiveSql
{
    public static readonly SqlStatement Find = new("codegen.archive.find", """
        SELECT Id, SchemaJson, SchemaSha256, GeneratorVersion, ManifestSha256, ZipSha256, FileName, ByteLength, ExpiresAtUtc
        FROM fn_codegeneration_run_archive WHERE Id = @Id
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement Insert = new("codegen.archive.insert", """
        INSERT INTO fn_codegeneration_run_archive
            (Id, SchemaJson, SchemaSha256, GeneratorVersion, ManifestSha256, ZipSha256, FileName, ByteLength, ExpiresAtUtc)
        VALUES (@Id, @SchemaJson, @SchemaSha256, @GeneratorVersion, @ManifestSha256, @ZipSha256, @FileName, @ByteLength, @ExpiresAtUtc)
        """, SqlDataScope.HostOnly);
}
