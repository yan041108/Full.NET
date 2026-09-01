using Full.NET.Abstractions.Messaging;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.Modules.Tenancy.Features.GetCurrentTenant;

/// <summary>
/// 读取当前请求已建立租户上下文的权威摘要；未解析出租户时应由上游边界决定返回匿名或 Host 语义。
/// </summary>
internal sealed record GetCurrentTenantQuery : IQuery<TenantSummary>;
