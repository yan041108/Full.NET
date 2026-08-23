using System.Text.Json.Serialization;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.CodeGeneration.Naming;

namespace Full.NET.Data.CodeGeneration.Serialization;

/// <summary>
/// 代码生成工具链持久化 JSON 的源生成闭包；与 HTTP Preview 上下文分离，覆盖 Naming Profile 与生成清单。
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(NamingProfile))]
[JsonSerializable(typeof(DatabaseNamingProfile))]
[JsonSerializable(typeof(ContractNamingProfile))]
[JsonSerializable(typeof(DotNetNamingProfile))]
[JsonSerializable(typeof(PatternProfile))]
[JsonSerializable(typeof(ConstraintDigestProfile))]
[JsonSerializable(typeof(GenerationManifestEntry))]
[JsonSerializable(typeof(GenerationManifestDocument))]
[JsonSerializable(typeof(GenerationManifestEntry[]))]
[JsonSerializable(typeof(GenerationRollbackCheckpointDocument))]
[JsonSerializable(typeof(GenerationRollbackCheckpointArtifactDocument))]
[JsonSerializable(typeof(GenerationRollbackCheckpointArtifactDocument[]))]
internal partial class CodeGenerationToolchainJsonSerializerContext : JsonSerializerContext;
