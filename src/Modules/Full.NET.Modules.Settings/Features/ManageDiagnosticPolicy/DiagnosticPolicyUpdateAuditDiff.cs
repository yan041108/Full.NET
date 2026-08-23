namespace Full.NET.Modules.Settings.Features.ManageDiagnosticPolicy;

/// <summary>诊断策略更新审计摘要；供源生成 JSON 序列化。</summary>
internal sealed record DiagnosticPolicyUpdateAuditDiff(
    long Version,
    string Pressure);
