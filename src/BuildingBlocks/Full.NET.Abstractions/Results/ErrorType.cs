namespace Full.NET.Abstractions.Results;

/// <summary>
/// 定义稳定的错误分类，用于跨层映射 HTTP 状态码、重试策略和错误展示。
/// </summary>
public enum ErrorType
{
    /// <summary>
    /// 输入参数或请求体不满足验证规则，属于调用方可纠正的客户端错误。
    /// </summary>
    Validation,
    /// <summary>
    /// 调用方未认证或认证凭证无效，需要先完成身份认证。
    /// </summary>
    Unauthorized,
    /// <summary>
    /// 调用方已认证但缺少执行该操作所需的权限。
    /// </summary>
    Forbidden,
    /// <summary>
    /// 请求的资源不存在，或调用方无权限感知其存在。
    /// </summary>
    NotFound,
    /// <summary>
    /// 操作与当前资源状态冲突，如并发修改、唯一性约束违反或重复创建。
    /// </summary>
    Conflict,
    /// <summary>
    /// 违反业务规则或领域不变量，属于业务语义层面的可预期失败。
    /// </summary>
    BusinessRule,
    /// <summary>
    /// 请求因频率限制被拒绝，调用方应按回退策略重试。
    /// </summary>
    RateLimited,
    /// <summary>
    /// 未分类的意外错误，通常对应系统内部异常，不应直接暴露细节给客户端。
    /// </summary>
    Unexpected
}
