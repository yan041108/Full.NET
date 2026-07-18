namespace Full.NET.Modularity.Modules;

/// <summary>
/// 定义宿主请求管道中供模块贡献中间件的固定插入点。
/// </summary>
/// <remarks>
/// 阶段是稳定契约：宿主在每个阶段按模块依赖顺序统一调用一次，模块据此选择注册时机。
/// 新增阶段属于契约扩展，必须同步宿主插入点与相关架构测试。
/// </remarks>
public enum ModulePipelineStage
{
    /// <summary>认证中间件之前，用于早期请求整形或前置校验。</summary>
    BeforeAuthentication,

    /// <summary>认证之后、授权之前，用于依赖已认证身份但需在授权前建立的上下文（如租户解析）。</summary>
    BeforeAuthorization,

    /// <summary>授权之后、映射 Endpoint 之前，用于依赖授权结果的收尾中间件。</summary>
    BeforeEndpoints,
}
