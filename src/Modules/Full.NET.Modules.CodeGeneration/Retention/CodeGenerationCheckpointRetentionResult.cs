namespace Full.NET.Modules.CodeGeneration.Retention;

internal sealed record CodeGenerationCheckpointRetentionResult(
    int Scanned,
    int Deleted,
    int Skipped,
    int Failed)
{
    public static CodeGenerationCheckpointRetentionResult Empty { get; } = new(0, 0, 0, 0);
}