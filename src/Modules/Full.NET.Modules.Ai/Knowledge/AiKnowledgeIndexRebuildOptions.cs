namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>所有 API/Worker 升级到多代次兼容版本后，才允许显式开启重建入口。</summary>
internal sealed class AiKnowledgeIndexRebuildOptions
{
    internal const string SectionName = "FullNet:Ai:KnowledgeIndexRebuild";
    /// <summary>默认关闭，不影响现有任务执行或活动代次读取。</summary>
    public bool Enabled { get; set; }
}
