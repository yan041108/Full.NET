namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>原会话增量收费派发开关；API 与 Worker 使用同一配置，默认关闭，修改后重启生效。</summary>
internal sealed class AiKnowledgeIndexGrantOptions
{
    internal const string SectionName = "FullNet:Ai:KnowledgeIndexGrants";
    /// <summary>显式开启后仍需逐知识库所有者授权及每阶段原会话复核。</summary>
    public bool Enabled { get; set; }
    /// <summary>每轮最多尝试的授权数，一至十六，默认四。</summary>
    public int BatchSize { get; set; } = 4;
    /// <summary>轮询间隔，一千至六万毫秒，默认一万。</summary>
    public int PollMilliseconds { get; set; } = 10000;
}
