namespace Full.NET.Composition;

/// <summary>
/// 定义 Full.NET 官方宿主可选择的显式模块装配范围。
/// </summary>
public enum FullNetHostProfile
{
    /// <summary>承载完整 HTTP 模块与 Endpoint 的 API 宿主。</summary>
    Api,

    /// <summary>只承载后台消费者最小依赖的 Worker 宿主。</summary>
    Worker,

    /// <summary>承载迁移、初始化领域服务且不映射 Endpoint 的 Migrator 宿主。</summary>
    Migrator,
}
