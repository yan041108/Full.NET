namespace Full.NET.AI.Abstractions.Connectivity;

/// <summary>供应商探测摘要；目录探测不证明推理权限、配额或模型实际可调用。</summary>
/// <param name="Succeeded">是否满足该供应商的目录探测条件。</param>
/// <param name="Message">由适配器生成的安全信息，禁止透传远端正文。</param>
public sealed record ModelConnectivityResult(bool Succeeded, string Message);
