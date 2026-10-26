namespace Full.NET.Modules.Workflow.Contracts;

/// <summary>业务所有者声明只能经受信端口发起的审批类型；HTTP请求不能冒充业务模块建立关联。</summary>
/// <param name="BusinessType">与所有者终态消费者完全一致的稳定业务类型。</param>
/// <param name="OwnerKey">声明归属的稳定模块键，仅由服务端模块注册。</param>
public sealed record WorkflowBusinessTypeOwnership(string BusinessType, string OwnerKey);
