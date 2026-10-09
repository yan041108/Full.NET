namespace Full.NET.Modules.Ai.Runtime;

/// <summary>运行固定到精确不可变版本及停用代次；旧运行 JSON 缺少此字段仍走原协议。</summary>
internal sealed record AiPromptRunBinding(Guid PromptId, Guid VersionId, int Version, string BodyHash, int Epoch);
